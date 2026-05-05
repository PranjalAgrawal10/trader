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
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ITwoFactorTotpHelper totp,
        ITwoFactorLoginTicketService twoFactorLoginTicket,
        IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _totp = totp;
        _twoFactorLoginTicket = twoFactorLoginTicket;
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

    public async Task<AuthResponse> CompleteTwoFactorLoginAsync(TwoFactorLoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TwoFactorToken) || string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Two-factor token and authenticator code are required.");

        var ticketStatus = _twoFactorLoginTicket.ValidatePendingLoginTicket(request.TwoFactorToken.Trim(), out var userId);
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

        if (!_totp.VerifyCode(secret, request.Code))
            throw new InvalidOperationException("Invalid authenticator code.");

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
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

    public async Task ConfirmTwoFactorEnrollmentAsync(Guid userId, TwoFactorConfirmRequest request, CancellationToken ct = default)
    {
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
            throw new InvalidOperationException("Could not read pending enrollment. Start setup again.");
        }

        if (!_totp.VerifyCode(secret, request.Code))
            throw new InvalidOperationException("Invalid authenticator code.");

        user.TotpSecretProtected = user.TotpPendingSecretProtected;
        user.TotpPendingSecretProtected = null;
        user.TwoFactorEnabled = true;
        await _users.SaveChangesAsync(ct);
    }

    public async Task CancelTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        user.TotpPendingSecretProtected = null;
        await _users.SaveChangesAsync(ct);
    }

    public async Task DisableTwoFactorAsync(Guid userId, TwoFactorDisableRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (!user.TwoFactorEnabled)
            throw new InvalidOperationException("Two-factor authentication is not enabled.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new InvalidOperationException("Invalid password.");

        if (string.IsNullOrEmpty(user.TotpSecretProtected))
            throw new InvalidOperationException("Two-factor data is inconsistent. Contact support.");

        byte[] secret;
        try
        {
            secret = _totp.UnprotectSecret(user.TotpSecretProtected);
        }
        catch
        {
            throw new InvalidOperationException("Could not read two-factor secret.");
        }

        if (!_totp.VerifyCode(secret, request.Code))
            throw new InvalidOperationException("Invalid authenticator code.");

        user.TwoFactorEnabled = false;
        user.TotpSecretProtected = null;
        user.TotpPendingSecretProtected = null;
        await _users.SaveChangesAsync(ct);
    }

    public async Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        return new TwoFactorStatusResponse(
            user.TwoFactorEnabled,
            !string.IsNullOrEmpty(user.TotpPendingSecretProtected));
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        return user;
    }
}
