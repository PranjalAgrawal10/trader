namespace Trader.Domain.Entities;

public sealed class UserLoginAudit
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset LoggedInAtUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? ForwardedFor { get; set; }
    public string? UserAgent { get; set; }
    public string? IpInfoJson { get; set; }

    public User User { get; set; } = null!;
}
