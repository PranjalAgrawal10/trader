using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

/// <summary>Logs email provider readiness at startup.</summary>
public sealed class SmtpStartupValidator : IHostedService
{
    private readonly SmtpOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SmtpStartupValidator> _logger;

    public SmtpStartupValidator(
        IOptions<SmtpOptions> options,
        IHostEnvironment environment,
        ILogger<SmtpStartupValidator> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.UsesSendGridApi)
        {
            var hasFrom = !string.IsNullOrWhiteSpace(_options.FromEmail ?? _options.User);
            if (!hasFrom)
            {
                _logger.LogError(
                    "SendGrid API key is set but {Section}__FromEmail is missing.",
                    SmtpOptions.SectionName);
                return Task.CompletedTask;
            }

            _logger.LogInformation(
                "Outbound email via SendGrid API (from {FromEmail}).",
                (_options.FromEmail ?? _options.User)!.Trim());
            return Task.CompletedTask;
        }

        if (!_options.IsEnabled)
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "No outbound email provider ({Section}__IsEnabled=false, no SendGrid key). " +
                    "Development will log email bodies to the console only.",
                    SmtpOptions.SectionName);
            }
            else
            {
                _logger.LogError(
                    "No outbound email provider. Set {Section}__SendGridApiKey (recommended on App Platform) " +
                    "or enable SMTP ({Section}__IsEnabled=true with host/user/password/from).",
                    SmtpOptions.SectionName,
                    SmtpOptions.SectionName);
            }

            return Task.CompletedTask;
        }

        var hasUser = !string.IsNullOrWhiteSpace(_options.User ?? _options.FromEmail);
        var hasPassword = !string.IsNullOrWhiteSpace(_options.Password);
        var hasFromEmail = !string.IsNullOrWhiteSpace(_options.FromEmail ?? _options.User);

        if (!hasUser || !hasPassword || !hasFromEmail)
        {
            _logger.LogError(
                "SMTP is enabled but incomplete: User={HasUser}, Password={HasPassword}, FromEmail={HasFrom}. " +
                "Set {Section}__User, {Section}__Password, and {Section}__FromEmail (Gmail: use an App password).",
                hasUser,
                hasPassword,
                hasFromEmail,
                SmtpOptions.SectionName,
                SmtpOptions.SectionName,
                SmtpOptions.SectionName);
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Outbound email via SMTP {Host}:{Port} (user {User}, TLS {Tls}).",
            _options.Host.Trim(),
            _options.Port,
            (_options.User ?? _options.FromEmail)!.Trim(),
            _options.EnableTls);

        if (!_environment.IsDevelopment() &&
            _options.Host.Contains("gmail.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Gmail SMTP on App Platform often fails to deliver (auth or blocking). " +
                "Set {Section}__SendGridApiKey and {Section}__FromEmail instead of Gmail SMTP.",
                SmtpOptions.SectionName,
                SmtpOptions.SectionName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
