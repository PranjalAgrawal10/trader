using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;
using Trader.Application.Exceptions;
using Trader.Application.Security;
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
    private readonly IPlainTextEmailSender _emailSender;
    private readonly IEmailOtpService _emailOtp;
    private readonly IUserLoginAuditRepository _loginAudits;
    private readonly JwtOptions _jwtOptions;
    private readonly AuthOptions _authOptions;
    private readonly PublicWebOptions _publicWeb;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ITwoFactorTotpHelper totp,
        ITwoFactorLoginTicketService twoFactorLoginTicket,
        ITwoFactorOtpAttemptLimiter attemptLimiter,
        ITwoFactorRecoveryCodesHelper recoveryCodes,
        IPlainTextEmailSender emailSender,
        IEmailOtpService emailOtp,
        IUserLoginAuditRepository loginAudits,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AuthOptions> authOptions,
        IOptions<PublicWebOptions> publicWeb)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _totp = totp;
        _twoFactorLoginTicket = twoFactorLoginTicket;
        _attemptLimiter = attemptLimiter;
        _recoveryCodes = recoveryCodes;
        _emailSender = emailSender;
        _emailOtp = emailOtp;
        _loginAudits = loginAudits;
        _jwtOptions = jwtOptions.Value;
        _authOptions = authOptions.Value;
        _publicWeb = publicWeb.Value;
    }

    private string RequireConfiguredFrontendOrigin()
    {
        var trimmed = (_publicWeb.FrontendBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Configure **`PublicWeb:FrontendBaseUrl`** so verification and password-reset links can be sent by email.");

        return trimmed.TrimEnd('/');
    }

    private string BuildFrontendLink(string configuredPathSegment, string queryParamName, string rawTokenValue)
    {
        var baseUrl = RequireConfiguredFrontendOrigin();
        var path = (configuredPathSegment ?? string.Empty).Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        var sep = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{path}{sep}{Uri.EscapeDataString(queryParamName)}={Uri.EscapeDataString(rawTokenValue)}";
    }

    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _users.ExistsByEmailAsync(normalizedEmail, ct))
            throw new ConflictException("Email is already registered.");

        _ = RequireConfiguredFrontendOrigin();

        var rawToken = OpaqueTokenHasher.CreateUrlToken();
        var tokenHash = OpaqueTokenHasher.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.Trader,
            CreatedAt = now,
            EmailVerifiedAtUtc = null,
            EmailVerificationTokenHash = tokenHash,
            EmailVerificationExpiresAtUtc = now.AddHours(EffectiveHours(_authOptions.EmailVerificationExpiryHours)),
            PasswordResetTokenHash = null,
            PasswordResetExpiresAtUtc = null,
            SecondFactorMethod = SecondFactorMethod.None,
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        var verifyUrl = BuildFrontendLink(_publicWeb.VerifyEmailPath, "token", rawToken);
        var subject = "Verify your Trader account email";
        var body =
            $"Confirm your email address using this secure link:\n\n{verifyUrl}\n\n" +
            $"This link expires in {EffectiveHours(_authOptions.EmailVerificationExpiryHours)} hours. If you did not create an account, ignore this email.";

        await _emailSender.SendPlainTextAsync(normalizedEmail, subject, body, ct);
        return new RegistrationPendingEmailVerification(normalizedEmail);
    }

    public async Task<AuthResponse> VerifyRegistrationEmailAsync(VerifyEmailRequest request, CancellationToken ct = default)
    {
        if (request.Token is null || string.IsNullOrWhiteSpace(request.Token))
            throw new InvalidOperationException("token is required.");

        var hash = OpaqueTokenHasher.HashToken(request.Token.Trim());
        var user = await _users.GetByEmailVerificationTokenHashAsync(hash, ct);
        if (user is null)
            throw new InvalidOperationException("Invalid or expired verification link.");

        if (user.EmailVerifiedAtUtc.HasValue)
            return IssueToken(user);

        if (user.EmailVerificationExpiresAtUtc is null || user.EmailVerificationExpiresAtUtc < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invalid or expired verification link.");

        user.EmailVerifiedAtUtc = DateTimeOffset.UtcNow;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationExpiresAtUtc = null;
        await _users.SaveChangesAsync(ct);
        return IssueToken(user);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        if (request.Email is null || string.IsNullOrWhiteSpace(request.Email))
            return;

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail, ct);
        if (user is null)
            return;

        _ = RequireConfiguredFrontendOrigin();

        var raw = OpaqueTokenHasher.CreateUrlToken();
        user.PasswordResetTokenHash = OpaqueTokenHasher.HashToken(raw);
        user.PasswordResetExpiresAtUtc =
            DateTimeOffset.UtcNow.AddHours(EffectiveHours(_authOptions.PasswordResetExpiryHours));
        await _users.SaveChangesAsync(ct);

        var resetUrl = BuildFrontendLink(_publicWeb.ResetPasswordPath, "token", raw);
        var subject = "Reset your Trader password";
        var body =
            $"Open this link to set a new password:\n\n{resetUrl}\n\n" +
            $"This link expires in {EffectiveHours(_authOptions.PasswordResetExpiryHours)} hours. If you did not request a reset, ignore this email.";

        await _emailSender.SendPlainTextAsync(normalizedEmail, subject, body, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (request.Token is null || string.IsNullOrWhiteSpace(request.Token))
            throw new InvalidOperationException("token is required.");

        var newPassword = request.NewPassword?.Trim() ?? string.Empty;
        if (newPassword.Length < 6)
            throw new InvalidOperationException("Password must be at least 6 characters.");

        var hash = OpaqueTokenHasher.HashToken(request.Token.Trim());
        var user = await _users.GetByPasswordResetTokenHashAsync(hash, ct);
        if (user is null)
            throw new InvalidOperationException("Invalid or expired password reset link.");

        if (user.PasswordResetExpiresAtUtc is null || user.PasswordResetExpiresAtUtc < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invalid or expired password reset link.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAtUtc = null;
        await _users.SaveChangesAsync(ct);
    }

    public async Task ResendLoginSecondFactorOtpAsync(ResendLoginOtpRequest request, CancellationToken ct = default)
    {
        var ticket = request.TempToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ticket))
            throw new InvalidOperationException("temp_token is required.");

        var ticketStatus = _twoFactorLoginTicket.ValidatePendingLoginTicket(ticket, out var userId);
        if (ticketStatus != TwoFactorPendingTicketStatus.Valid)
            throw new InvalidOperationException("Sign-in session is not valid anymore. Sign in again with your password.");

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null ||
            !user.TwoFactorEnabled ||
            user.SecondFactorMethod != SecondFactorMethod.EmailSignInCode ||
            !user.EmailVerifiedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Cannot resend code for this sign-in session.");
        }

        await _emailOtp.SendLoginSecondFactorAsync(user.Email, ct);
    }

    public async Task EnableEmailSignInSecondFactorAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (!user.EmailVerifiedAtUtc.HasValue)
            throw new InvalidOperationException("Verify your email before enabling security options.");

        if (user.TwoFactorEnabled &&
            user.SecondFactorMethod == SecondFactorMethod.AuthenticatorApp &&
            !string.IsNullOrEmpty(user.TotpSecretProtected))
        {
            throw new ConflictException("Turn off authenticator-based 2FA before switching to email sign-in codes.");
        }

        if (!string.IsNullOrEmpty(user.TotpPendingSecretProtected))
        {
            user.TotpPendingSecretProtected = null;
            _attemptLimiter.Reset(EnrollScope(userId));
        }

        user.TotpSecretProtected = null;
        user.TotpRecoveryCodesProtected = null;
        user.SecondFactorMethod = SecondFactorMethod.EmailSignInCode;
        user.TwoFactorEnabled = true;
        await _users.SaveChangesAsync(ct);
    }

    public async Task<ProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        return new ProfileResponse(
            user.Id,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.EmailVerifiedAtUtc.HasValue);
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        AuthRequestContext? requestContext = null,
        CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail, ct);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return new LoginRejected();

        if (!user.EmailVerifiedAtUtc.HasValue)
            return new LoginRequiresEmailVerification();

        if (user.TwoFactorEnabled)
        {
            if (user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
            {
                var ticket = _twoFactorLoginTicket.CreatePendingLoginTicket(user.Id);
                await _emailOtp.SendLoginSecondFactorAsync(user.Email, ct);
                return new LoginRequiresTwoFactor(ticket, "email_otp");
            }

            if (string.IsNullOrEmpty(user.TotpSecretProtected))
                return new LoginRejected();

            var totpTicket = _twoFactorLoginTicket.CreatePendingLoginTicket(user.Id);
            return new LoginRequiresTwoFactor(totpTicket, "authenticator");
        }

        var auth = await IssueTokenWithLoginAuditAsync(user, requestContext, ct).ConfigureAwait(false);
        return new LoginSucceeded(auth);
    }

    public async Task<AuthResponse> CompleteTwoFactorLoginAsync(
        TwoFactorVerifyLoginRequest request,
        AuthRequestContext? requestContext = null,
        CancellationToken ct = default)
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
        if (user is null ||
            !user.TwoFactorEnabled ||
            !user.EmailVerifiedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "Could not complete sign-in. Use Back and sign in again with your password.");
        }

        if (user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
        {
            var verify =
                await _emailOtp.VerifyAsync(new EmailOtpVerifyRequest { Email = user.Email, Otp = code }, ct);
            if (!verify.Verified)
            {
                _attemptLimiter.RegisterFailure(scope);
                throw new InvalidOperationException("Invalid or expired email sign-in code.");
            }

            _attemptLimiter.Reset(scope);
            return await IssueTokenWithLoginAuditAsync(user, requestContext, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(user.TotpSecretProtected))
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
            return await IssueTokenWithLoginAuditAsync(user, requestContext, ct).ConfigureAwait(false);
        }

        _attemptLimiter.RegisterFailure(scope);
        throw new InvalidOperationException("Invalid authenticator or recovery code.");
    }

    public async Task<TwoFactorEnrollmentResponse> BeginTwoFactorEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (user.TwoFactorEnabled &&
            user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
        {
            throw new ConflictException("Turn off email sign-in verification before enrolling an authenticator app.");
        }

        if (!user.EmailVerifiedAtUtc.HasValue)
            throw new InvalidOperationException("Verify your email before enrolling an authenticator app.");

        if (user.TwoFactorEnabled && user.SecondFactorMethod == SecondFactorMethod.AuthenticatorApp)
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
        if (user.TwoFactorEnabled &&
            user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
        {
            throw new ConflictException("Turn off email sign-in verification before enrolling an authenticator app.");
        }

        if (!user.EmailVerifiedAtUtc.HasValue)
            throw new InvalidOperationException("Verify your email before enrolling an authenticator app.");

        if (user.TwoFactorEnabled && user.SecondFactorMethod == SecondFactorMethod.AuthenticatorApp)
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
        user.SecondFactorMethod = SecondFactorMethod.AuthenticatorApp;
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

        var password = request.Password?.Trim();

        if (user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Enter your current password to turn off email sign-in verification.");

            if (!_passwordHasher.Verify(password, user.PasswordHash))
                throw new InvalidOperationException("Incorrect password.");

            ClearTwoFactor(user);
            _attemptLimiter.Reset(DisableScope(userId));
            await _users.SaveChangesAsync(ct);
            return;
        }

        if (string.IsNullOrEmpty(user.TotpSecretProtected))
            throw new InvalidOperationException("Two-factor data is inconsistent. Contact support.");

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

    /// <summary>Clears stored second-factor sign-in preferences; caller must save.</summary>
    private static void ClearTwoFactor(User user)
    {
        user.TwoFactorEnabled = false;
        user.SecondFactorMethod = SecondFactorMethod.None;
        user.TotpSecretProtected = null;
        user.TotpPendingSecretProtected = null;
        user.TotpRecoveryCodesProtected = null;
    }

    public async Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);

        string methodDesc;
        if (!user.TwoFactorEnabled || user.SecondFactorMethod == SecondFactorMethod.None)
            methodDesc = "none";
        else if (user.SecondFactorMethod == SecondFactorMethod.EmailSignInCode)
            methodDesc = "email_otp";
        else if (user.SecondFactorMethod == SecondFactorMethod.AuthenticatorApp)
            methodDesc = "authenticator";
        else
            methodDesc = "none";

        return new TwoFactorStatusResponse(
            user.TwoFactorEnabled,
            !string.IsNullOrEmpty(user.TotpPendingSecretProtected),
            methodDesc);
    }

    private static int EffectiveHours(int requested) =>
        requested switch
        {
            < 1 => 48,
            > 168 => 168,
            _ => requested,
        };

    private AuthResponse IssueToken(User user)
    {
        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
    }

    private async Task<AuthResponse> IssueTokenWithLoginAuditAsync(
        User user,
        AuthRequestContext? requestContext,
        CancellationToken ct)
    {
        var auth = IssueToken(user);
        var ip = NormalizeIpAddress(requestContext?.IpAddress);
        var forwardedFor = TrimToMaxOrNull(requestContext?.ForwardedFor, 1024);
        var userAgent = TrimToMaxOrNull(requestContext?.UserAgent, 512);
        var ipInfoJson = TrimToMaxOrNull(requestContext?.IpInfoJson, 4000);
        var fallbackIp = "unknown";

        await _loginAudits.AddAsync(
                new UserLoginAudit
                {
                    UserId = user.Id,
                    LoggedInAtUtc = DateTimeOffset.UtcNow,
                    IpAddress = ip ?? fallbackIp,
                    ForwardedFor = forwardedFor,
                    UserAgent = userAgent,
                    IpInfoJson = ipInfoJson,
                },
                ct)
            .ConfigureAwait(false);
        await _loginAudits.SaveChangesAsync(ct).ConfigureAwait(false);
        return auth;
    }

    private static string? NormalizeIpAddress(string? raw)
    {
        return TrimToMaxOrNull(raw, 64);
    }

    private static string? TrimToMaxOrNull(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        return value.Length <= maxLen ? value : value[..maxLen];
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
