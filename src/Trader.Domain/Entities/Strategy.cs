namespace Trader.Domain.Entities;

public class Strategy
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Bot> Bots { get; set; } = new List<Bot>();
}
