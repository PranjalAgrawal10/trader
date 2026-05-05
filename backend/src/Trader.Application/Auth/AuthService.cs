using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;
using Trader.Application.Exceptions;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITwoFactorTotpHelper _totp;
    private readonly ITwoFactorLoginTicketService _twoFactorLoginTicket;
    private readonly ITwoFactorOtpAttemptLimiter _attemptLimiter;
    private readonly ITwoFactorRecoveryCodesHelper _recoveryCodes;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ITwoFactorTotpHelper totp,
        ITwoFactorLoginTicketService twoFactorLoginTicket,
        ITwoFactorOtpAttemptLimiter attemptLimiter,
        ITwoFactorRecoveryCodesHelper recoveryCodes,
        IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _totp = totp;
        _twoFactorLoginTicket = twoFactorLoginTicket;
        _attemptLimiter = attemptLimiter;
        _recoveryCodes = recoveryCodes;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _users.ExistsByEmailAsync(normalizedEmail, ct))
            throw new ConflictException("Email is already registered.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.Trader,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail, ct);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return new LoginRejected();

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(user.TotpSecretProtected))
                return new LoginRejected();

            var ticket = _twoFactorLoginTicket.CreatePendingLoginTicket(user.Id);
            return new LoginRequiresTwoFactor(ticket);
        }

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new LoginSucceeded(new AuthResponse(token, user.Id, user.Email, user.Role));
    }

    public async Task<AuthResponse> CompleteTwoFactorLoginAsync(TwoFactorVerifyLoginRequest request, CancellationToken ct = default)
    {
        var ticket = request.ResolveTicket();
        var code = request.ResolveCode();
        if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("temp_token and otp are required.");

        var ticketStatus = _twoFactorLoginTicket.ValidatePendingLoginTicket(ticket, out var userId);
        if (ticketStatus == TwoFactorPendingTicketStatus.Expired)
        {
            throw new InvalidOperationException(
                "This sign-in step expired. Use Back and sign in again with your password.");
        }

        if (ticketStatus != TwoFactorPendingTicketStatus.Valid)
        {
            throw new InvalidOperationException(
                "This sign-in step is not valid. Use Back and sign in again with your password.");
        }

        var scope = LoginScope(userId);
        _attemptLimiter.EnsureNotBlocked(scope);

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.TwoFactorEnabled || string.IsNullOrEmpty(user.TotpSecretProtected))
        {
            throw new InvalidOperationException(
                "Could not complete sign-in. Use Back and sign in again with your password.");
        }

        byte[] secret;
        try
        {
            secret = _totp.UnprotectSecret(user.TotpSecretProtected);
        }
        catch
        {
            throw new InvalidOperationException(
                "Could not read two-factor data. Use Back and sign in again with your password.");
        }

        if (_totp.VerifyCode(secret, code))
        {
            _attemptLimiter.Reset(scope);
            return IssueToken(user);
        }

        if (_recoveryCodes.TryConsumeOne(user.TotpRecoveryCodesProtected, code, out var updatedPayload))
        {
            user.TotpRecoveryCodesProtected = updatedPayload;
            await _users.SaveChangesAsync(ct);
            _attemptLimiter.Reset(scope);
            return IssueToken(user);
        }

        _attemptLimiter.RegisterFailure(scope);
        throw new InvalidOperationException("Invalid authenticator or recovery code.");
    }

    public async Task<TwoFactorEnrollmentResponse> BeginTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (user.TwoFactorEnabled)
            throw new ConflictException("Two-factor authentication is already enabled.");

        var secret = _totp.GenerateSecretKey();
        user.TotpPendingSecretProtected = _totp.ProtectSecret(secret);
        await _users.SaveChangesAsync(ct);

        var issuer = string.IsNullOrWhiteSpace(_jwtOptions.Issuer) ? "Trader" : _jwtOptions.Issuer;
        var manual = _totp.ToBase32Key(secret);
        var uri = _totp.BuildOtpAuthUri(user.Email, secret, issuer);
        return new TwoFactorEnrollmentResponse(manual, uri);
    }

    public async Task<TwoFactorEnrollmentConfirmResult> ConfirmTwoFactorEnrollmentAsync(
        Guid userId,
        TwoFactorConfirmRequest request,
        CancellationToken ct = default)
    {
        var scope = EnrollScope(userId);
        _attemptLimiter.EnsureNotBlocked(scope);

        var user = await RequireUserAsync(userId, ct);
        if (user.TwoFactorEnabled)
            throw new ConflictException("Two-factor authentication is already enabled.");

        if (string.IsNullOrEmpty(user.TotpPendingSecretProtected))
            throw new InvalidOperationException("No pending two-factor enrollment. Start setup again.");

        byte[] secret;
        try
        {
            secret = _totp.UnprotectSecret(user.TotpPendingSecretProtected);
        }
        catch
        {
            _attemptLimiter.RegisterFailure(scope);
            throw new InvalidOperationException("Could not read pending enrollment. Start setup again.");
        }

        if (!_totp.VerifyCode(secret, request.ResolveCode()))
        {
            _attemptLimiter.RegisterFailure(scope);
            throw new InvalidOperationException("Invalid authenticator code.");
        }

        _attemptLimiter.Reset(scope);

        var (plaintext, protectedPayload) = _recoveryCodes.IssueNewProtectedPayload();
        user.TotpSecretProtected = user.TotpPendingSecretProtected;
        user.TotpPendingSecretProtected = null;
        user.TotpRecoveryCodesProtected = protectedPayload;
        user.TwoFactorEnabled = true;
        await _users.SaveChangesAsync(ct);

        return new TwoFactorEnrollmentConfirmResult(plaintext);
    }

    public async Task CancelTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        user.TotpPendingSecretProtected = null;
        _attemptLimiter.Reset(EnrollScope(userId));
        await _users.SaveChangesAsync(ct);
    }

    public async Task DisableTwoFactorAsync(Guid userId, TwoFactorDisableRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (!user.TwoFactorEnabled)
            throw new InvalidOperationException("Two-factor authentication is not enabled.");

        if (string.IsNullOrEmpty(user.TotpSecretProtected))
            throw new InvalidOperationException("Two-factor data is inconsistent. Contact support.");

        var password = request.Password?.Trim();
        if (!string.IsNullOrEmpty(password) && _passwordHasher.Verify(password, user.PasswordHash))
        {
            ClearTwoFactor(user);
            _attemptLimiter.Reset(DisableScope(userId));
            await _users.SaveChangesAsync(ct);
            return;
        }

        var otpOrRecovery = request.ResolveOtpOrCode();
        if (string.IsNullOrEmpty(otpOrRecovery))
        {
            throw new InvalidOperationException("Provide your current password or an authenticator/recovery code.");
        }

        var scope = DisableScope(userId);
        _attemptLimiter.EnsureNotBlocked(scope);

        byte[] secret;
        try
        {
            secret = _totp.UnprotectSecret(user.TotpSecretProtected);
        }
        catch
        {
            throw new InvalidOperationException("Could not read two-factor secret.");
        }

        if (_totp.VerifyCode(secret, otpOrRecovery))
        {
            ClearTwoFactor(user);
            _attemptLimiter.Reset(scope);
            await _users.SaveChangesAsync(ct);
            return;
        }

        if (_recoveryCodes.TryConsumeOne(user.TotpRecoveryCodesProtected, otpOrRecovery, out _))
        {
            ClearTwoFactor(user);
            _attemptLimiter.Reset(scope);
            await _users.SaveChangesAsync(ct);
            return;
        }

        _attemptLimiter.RegisterFailure(scope);
        throw new InvalidOperationException("Invalid password or authenticator/recovery code.");
    }

    /// <summary>Clears stored TOTP/recovery state; caller must save.</summary>
    private static void ClearTwoFactor(User user)
    {
        user.TwoFactorEnabled = false;
        user.TotpSecretProtected = null;
        user.TotpPendingSecretProtected = null;
        user.TotpRecoveryCodesProtected = null;
    }

    public async Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        return new TwoFactorStatusResponse(
            user.TwoFactorEnabled,
            !string.IsNullOrEmpty(user.TotpPendingSecretProtected));
    }

    private AuthResponse IssueToken(User user)
    {
        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
    }

    private static string LoginScope(Guid userId) => $"2fa-login:{userId:N}";

    private static string EnrollScope(Guid userId) => $"2fa-enroll:{userId:N}";

    private static string DisableScope(Guid userId) => $"2fa-disable:{userId:N}";

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        return user;
    }
}
