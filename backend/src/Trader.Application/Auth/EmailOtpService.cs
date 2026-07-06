using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Auth;

public sealed partial class EmailOtpService : IEmailOtpService
{
    private readonly IEmailOtpRepository _repository;
    private readonly IPlainTextEmailSender _emailSender;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailOtpOptions _otpOptions;
    private readonly ILogger<EmailOtpService> _logger;

    public EmailOtpService(
        IEmailOtpRepository repository,
        IPlainTextEmailSender emailSender,
        IPasswordHasher passwordHasher,
        IOptions<EmailOtpOptions> otpOptions,
        ILogger<EmailOtpService> logger)
    {
        _repository = repository;
        _emailSender = emailSender;
        _passwordHasher = passwordHasher;
        _otpOptions = otpOptions.Value;
        _logger = logger;
    }

    public Task SendAsync(EmailOtpSendRequest request, CancellationToken ct = default)
    {
        if (request.Email is null)
            throw new InvalidOperationException("A valid email is required.");

        var email = ValidateAndNormalizeEmail(request.Email);
        return SendOtpAsync(email, EmailOtpPurpose.Generic, ct);
    }

    public Task SendOtpAsync(string normalizedEmail, EmailOtpPurpose purpose, CancellationToken ct = default)
    {
        var email = ValidateAndNormalizeEmail(normalizedEmail);
        return SendSixDigitChallengeAsync(email, purpose, ct);
    }

    private async Task SendSixDigitChallengeAsync(
        string normalizedEmail,
        EmailOtpPurpose purpose,
        CancellationToken ct)
    {
        var template = EmailOtpTemplates.ForPurpose(purpose);
        var plainCode = GenerateSixDigitCode();
        var hash = _passwordHasher.Hash(plainCode);
        var expiryMinutes = EffectiveExpiryMinutes();
        var now = DateTimeOffset.UtcNow;
        var challenge = new EmailOtpChallenge
        {
            Id = Guid.NewGuid(),
            NormalizedEmail = normalizedEmail,
            Purpose = purpose,
            OtpHash = hash,
            ExpiresAtUtc = now.AddMinutes(expiryMinutes),
            IsConsumed = false,
            FailedVerifyAttempts = 0,
            CreatedAtUtc = now,
        };

        await _repository.InvalidatePendingForEmailAsync(normalizedEmail, purpose, ct);
        await _repository.AddAsync(challenge, ct);
        await _repository.SaveChangesAsync(ct);

        var plainBody = EmailOtpTemplates.BuildPlainBody(template, plainCode, expiryMinutes);
        var htmlBody = EmailOtpHtmlBuilder.Build(template, plainCode, expiryMinutes);

        try
        {
            await _emailSender.SendEmailAsync(
                    normalizedEmail,
                    template.Subject,
                    plainBody,
                    htmlBody,
                    Array.Empty<EmbeddedEmailImage>(),
                    Array.Empty<EmailAttachment>(),
                    ct)
                .ConfigureAwait(false);
        }
        catch
        {
            await _repository.DeleteByIdAsync(challenge.Id, ct);
            throw;
        }

        _logger.LogInformation(
            "Email OTP sent ({Purpose}) to {Recipient} subject={Subject}",
            purpose,
            normalizedEmail,
            template.Subject);
    }

    public async Task<EmailOtpVerifyResponse> VerifyAsync(
        EmailOtpVerifyRequest request,
        EmailOtpPurpose purpose = EmailOtpPurpose.Generic,
        CancellationToken ct = default)
    {
        if (request.Email is null || request.Otp is null)
            throw new InvalidOperationException("Email and OTP are required.");

        var email = ValidateAndNormalizeEmail(request.Email);
        var code = NormalizeOtpCode(request.Otp);
        var maxFails = EffectiveMaxFailures();

        if (code.Length != 6)
            return new EmailOtpVerifyResponse(false);

        var challenge = await _repository.GetLatestForEmailAsync(email, purpose, ct);
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
