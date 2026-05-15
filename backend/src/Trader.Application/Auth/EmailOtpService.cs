using System.Globalization;
using System.Net;
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

    /// <remarks>
    /// HTML clients cannot reliably run JavaScript Clipboard APIs; we avoid fake copy buttons and render clearly
    /// selectable OTP blocks with touch-friendly selection hints.
    /// </remarks>
    private static string BuildLoginSecondFactorHtmlBody(string plainCode, int expiryMinutes)
    {
        var codeEsc = WebUtility.HtmlEncode(plainCode);
        var expiryEsc = WebUtility.HtmlEncode(expiryMinutes.ToString(CultureInfo.InvariantCulture));

        return
            $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"></head>"
            + $"<body style=\"margin:0;padding:28px;background:#f4f6f8;color:#1a202c;"
            + "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;line-height:1.5;"
            + "font-size:16px;\">"
            + "<p style=\"margin:0 0 8px;font-weight:600;\">Trader sign-in</p>"
            + "<p style=\"margin:0 0 20px;color:#475569;\">Enter this verification code:</p>"
            + "<!-- Selectable OTP (mobile: tap twice or tap and hold → Select All → Copy). -->"
            + "<div tabindex=\"0\" aria-label=\"Sign-in verification code\""
            + " style=\"-webkit-touch-callout:default;-webkit-user-select:text;user-select:text;"
            + "margin:0 0 20px;padding:16px 20px;font-size:32px;line-height:1.2;font-weight:700;"
            + "letter-spacing:0.45em;text-align:center;font-family:Consolas,Menlo,'Courier New',monospace;"
            + "background:#fff;border-radius:14px;border:1px solid #cbd5e1;color:#0f172a;box-sizing:border-box;\">"
            + codeEsc + "</div>"
            + "<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin:0 0 24px;"
            + "border-collapse:separate;border-spacing:0;\">"
            + "<tr><td aria-label=\"Sign-in OTP (select and copy)\""
            + " style=\"-webkit-touch-callout:default;-webkit-user-select:text;user-select:text;"
            + "padding:14px 22px;border-radius:9999px;text-align:center;"
            + "background-color:#0f172a;background-image:linear-gradient(#1e293b,#0f172a);\">"
            + "<span style=\"color:#e2e8f0;font-size:13px;font-weight:700;"
            + "letter-spacing:0.18em;text-transform:uppercase;\">OTP code&nbsp;&nbsp;</span>"
            + "<span style=\"color:#f8fafc;font-size:22px;font-weight:800;"
            + "letter-spacing:0.32em;font-family:Consolas,Menlo,'Courier New',monospace;\">" + codeEsc + "</span>"
            + "</td></tr></table>"
            + "<p style=\"margin:-4px 0 16px;color:#64748b;font-size:13px;\">"
            + "To copy quickly: tap and hold either code block, then use <strong>Select All</strong> and "
            + "<strong>Copy</strong> from your mail app menu.</p>"
            + "<p style=\"margin:0 0 8px;color:#64748b;font-size:14px;\">Expires in "
            + $"<strong style=\"color:#334155;\">{expiryEsc}</strong> minute(s).</p>"
            + "<p style=\"margin:0;color:#64748b;font-size:13px;\">If you didn't attempt to sign in, change"
            + " your password and contact support.</p></body></html>";
    }

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
