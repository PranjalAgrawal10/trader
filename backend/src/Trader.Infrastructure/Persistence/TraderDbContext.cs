using Microsoft.EntityFrameworkCore;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence;

public sealed class TraderDbContext : DbContext
{
    public TraderDbContext(DbContextOptions<TraderDbContext> options)
        : base(options)
    {
    }

    public DbSet<EmailOtpChallenge> EmailOtpChallenges => Set<EmailOtpChallenge>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradingOrder> TradingOrders => Set<TradingOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailOtpChallenge>(e =>
        {
            e.Property(x => x.NormalizedEmail).HasMaxLength(320);
            e.Property(x => x.OtpHash).HasMaxLength(200);
            e.HasIndex(x => new { x.NormalizedEmail, x.IsConsumed });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(64);
            e.Property(x => x.BrokerConnectedAt).HasColumnType("datetime(6)");
            e.Property(x => x.BrokerProvider).HasMaxLength(64);
            e.Property(x => x.KiteUserId).HasMaxLength(64);
            e.Property(x => x.KiteAccessTokenProtected);
            e.Property(x => x.KiteRefreshTokenProtected);
            e.Property(x => x.TotpSecretProtected);
            e.Property(x => x.TotpPendingSecretProtected);
            e.Property(x => x.TotpRecoveryCodesProtected);
            e.Property(x => x.SecondFactorMethod).HasConversion<byte>();
            e.Property(x => x.EmailVerificationTokenHash).HasMaxLength(64);
            e.Property(x => x.PasswordResetTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.EmailVerificationTokenHash).IsUnique();
            e.HasIndex(x => x.PasswordResetTokenHash).IsUnique();
        });

        modelBuilder.Entity<Strategy>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany(u => u.Strategies).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bot>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.Bots).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Strategy).WithMany(s => s.Bots).HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Trade>(e =>
        {
            e.Property(x => x.Symbol).HasMaxLength(64);
            e.Property(x => x.Quantity).HasPrecision(28, 8);
            e.Property(x => x.Price).HasPrecision(28, 8);
            e.Property(x => x.RealizedPnl).HasPrecision(28, 8);
            e.HasOne(x => x.Bot).WithMany(b => b.Trades).HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TradingOrder>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.HasOne(x => x.Bot).WithMany(b => b.Orders).HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
