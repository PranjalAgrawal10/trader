using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

/// <summary>Logs SMTP readiness at startup so misconfigured production deploys are obvious in runtime logs.</summary>
public sealed class SmtpStartupValidator : IHostedService
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpStartupValidator> _logger;

    public SmtpStartupValidator(IOptions<SmtpOptions> options, ILogger<SmtpStartupValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogWarning(
                "SMTP is disabled ({Section}__IsEnabled=false). Registration, password reset, and email OTP will fail until SMTP is configured.",
                SmtpOptions.SectionName);
            return Task.CompletedTask;
        }

        var hasUser = !string.IsNullOrWhiteSpace(_options.User ?? _options.FromEmail);
        var hasPassword = !string.IsNullOrWhiteSpace(_options.Password);
        var hasFrom = !string.IsNullOrWhiteSpace(_options.FromEmail ?? _options.User);

        if (!hasUser || !hasPassword || !hasFrom)
        {
            _logger.LogError(
                "SMTP is enabled but incomplete: User={HasUser}, Password={HasPassword}, FromEmail={HasFrom}. " +
                "Set {Section}__User, {Section}__Password, and {Section}__FromEmail (Gmail: use an App password).",
                hasUser,
                hasPassword,
                hasFrom,
                SmtpOptions.SectionName,
                SmtpOptions.SectionName,
                SmtpOptions.SectionName);
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "SMTP enabled for {Host}:{Port} (user {User}, TLS {Tls}).",
            _options.Host.Trim(),
            _options.Port,
            (_options.User ?? _options.FromEmail)!.Trim(),
            _options.EnableTls);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
