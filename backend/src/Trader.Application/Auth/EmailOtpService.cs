using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;
using Trader.Domain.Entities;

namespace Trader.Application.Auth;

public sealed partial class EmailOtpService : IEmailOtpService
{
    private readonly IEmailOtpRepository _repository;
    private readonly IPlainTextEmailSender _emailSender;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailOtpOptions _otpOptions;

    public EmailOtpService(
        IEmailOtpRepository repository,
        IPlainTextEmailSender emailSender,
        IPasswordHasher passwordHasher,
        IOptions<EmailOtpOptions> otpOptions)
    {
        _repository = repository;
        _emailSender = emailSender;
        _passwordHasher = passwordHasher;
        _otpOptions = otpOptions.Value;
    }

    public Task SendAsync(EmailOtpSendRequest request, CancellationToken ct = default)
    {
        if (request.Email is null)
            throw new InvalidOperationException("A valid email is required.");

        var email = ValidateAndNormalizeEmail(request.Email);
        return SendSixDigitChallengeAsync(
            email,
            "Your verification code",
            (plainCode, expiryMinutes) =>
                $"Your verification code is: {plainCode}. It expires in {expiryMinutes} minutes. If you did not request this, you can ignore this email.",
            buildHtmlBody: null,
            ct);
    }

    public Task SendLoginSecondFactorAsync(string normalizedEmail, CancellationToken ct = default)
    {
        var email = ValidateAndNormalizeEmail(normalizedEmail);
        return SendSixDigitChallengeAsync(
            email,
            "Your Trader sign-in code",
            (plainCode, expiryMinutes) =>
                $"Your Trader sign-in code is: {plainCode}. It expires in {expiryMinutes} minutes. If you did not sign in, change your password and contact support.",
            BuildLoginSecondFactorHtmlBody,
            ct);
    }

    private static string BuildLoginSecondFactorHtmlBody(string plainCode, int expiryMinutes) =>
        LoginSecondFactorEmailHtmlBuilder.Build(plainCode, expiryMinutes);

    private async Task SendSixDigitChallengeAsync(
        string normalizedEmail,
        string subject,
        Func<string, int, string> buildPlainBody,
        Func<string, int, string>? buildHtmlBody,
        CancellationToken ct)
    {
        var plainCode = GenerateSixDigitCode();
        var hash = _passwordHasher.Hash(plainCode);
        var expiryMinutes = EffectiveExpiryMinutes();
        var now = DateTimeOffset.UtcNow;
        var challenge = new EmailOtpChallenge
        {
            Id = Guid.NewGuid(),
            NormalizedEmail = normalizedEmail,
            OtpHash = hash,
            ExpiresAtUtc = now.AddMinutes(expiryMinutes),
            IsConsumed = false,
            FailedVerifyAttempts = 0,
            CreatedAtUtc = now,
        };

        await _repository.InvalidatePendingForEmailAsync(normalizedEmail, ct);
        await _repository.AddAsync(challenge, ct);
        await _repository.SaveChangesAsync(ct);

        var body = buildPlainBody(plainCode, expiryMinutes);

        try
        {
            if (buildHtmlBody is not null)
            {
                var html = buildHtmlBody(plainCode, expiryMinutes);
                await _emailSender.SendEmailAsync(
                        normalizedEmail,
                        subject,
                        body,
                        html,
                        Array.Empty<EmbeddedEmailImage>(),
                        Array.Empty<EmailAttachment>(),
                        ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await _emailSender.SendPlainTextAsync(normalizedEmail, subject, body, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            await _repository.DeleteByIdAsync(challenge.Id, ct);
            throw;
        }
    }

    public async Task<EmailOtpVerifyResponse> VerifyAsync(EmailOtpVerifyRequest request, CancellationToken ct = default)
    {
        if (request.Email is null || request.Otp is null)
            throw new InvalidOperationException("Email and OTP are required.");

        var email = ValidateAndNormalizeEmail(request.Email);
        var code = NormalizeOtpCode(request.Otp);
        var maxFails = EffectiveMaxFailures();

        if (code.Length != 6)
            return new EmailOtpVerifyResponse(false);

        var challenge = await _repository.GetLatestForEmailAsync(email, ct);
        if (challenge is null)
            return new EmailOtpVerifyResponse(false);

        if (challenge.FailedVerifyAttempts >= maxFails)
            return new EmailOtpVerifyResponse(false);

        var ok = _passwordHasher.Verify(code, challenge.OtpHash);
        if (ok)
        {
            challenge.IsConsumed = true;
            await _repository.SaveChangesAsync(ct);
            return new EmailOtpVerifyResponse(true);
        }

        challenge.FailedVerifyAttempts++;
        if (challenge.FailedVerifyAttempts >= maxFails)
            challenge.IsConsumed = true;

        await _repository.SaveChangesAsync(ct);
        return new EmailOtpVerifyResponse(false);
    }

    private static string ValidateAndNormalizeEmail(string raw)
    {
        var email = raw.Trim().ToLowerInvariant();
        if (email.Length is 0 or > 320)
            throw new InvalidOperationException("A valid email is required.");

        if (!EmailRegex().IsMatch(email))
            throw new InvalidOperationException("A valid email is required.");

        return email;
    }

    private static string NormalizeOtpCode(string raw)
    {
        var s = raw.Trim();
        var digitsOnly = DigitOnlyRegex().Replace(s, string.Empty);
        return digitsOnly.Length > 0 ? digitsOnly : s;
    }

    private static string GenerateSixDigitCode()
    {
        var n = RandomNumberGenerator.GetInt32(100_000, 1_000_000);
        return n.ToString("D6", CultureInfo.InvariantCulture);
    }

    private int EffectiveExpiryMinutes() =>
        _otpOptions.ExpiryMinutes switch
        {
            >= 1 and <= 60 => _otpOptions.ExpiryMinutes,
            _ => 5,
        };

    private int EffectiveMaxFailures() =>
        _otpOptions.MaxFailedVerifyAttemptsPerChallenge switch
        {
            >= 1 and <= 20 => _otpOptions.MaxFailedVerifyAttemptsPerChallenge,
            _ => 5,
        };

    [GeneratedRegex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("\\D", RegexOptions.CultureInvariant)]
    private static partial Regex DigitOnlyRegex();
}
