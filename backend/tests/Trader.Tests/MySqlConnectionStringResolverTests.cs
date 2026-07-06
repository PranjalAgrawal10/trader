using Microsoft.Extensions.Configuration;
using Trader.Infrastructure.Persistence;

namespace Trader.Tests;

public sealed class MySqlConnectionStringResolverTests
{
    [Fact]
    public void Resolve_builds_from_discrete_database_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "db.example.com",
                ["Database:Name"] = "trader",
                ["Database:Username"] = "app",
                ["Database:Password"] = "secret",
                ["Database:Port"] = "25060",
                ["Database:SslMode"] = "Required",
            })
            .Build();

        var cs = MySqlConnectionStringResolver.Resolve(config);

        Assert.NotNull(cs);
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(cs);
        Assert.Equal("db.example.com", builder.Server);
        Assert.Equal("trader", builder.Database);
        Assert.Equal("app", builder.UserID);
        Assert.Equal("secret", builder.Password);
        Assert.Equal(25060u, builder.Port);
        Assert.Equal(MySqlConnector.MySqlSslMode.Required, builder.SslMode);
    }

    [Fact]
    public void Resolve_parses_mysql_database_url()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "mysql://doadmin:pass%21word@managed.db.ondigitalocean.com:25060/defaultdb?ssl-mode=Required",
            })
            .Build();

        var cs = MySqlConnectionStringResolver.Resolve(config);

        Assert.NotNull(cs);
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(cs);
        Assert.Equal("managed.db.ondigitalocean.com", builder.Server);
        Assert.Equal("defaultdb", builder.Database);
        Assert.Equal("doadmin", builder.UserID);
        Assert.Equal("pass!word", builder.Password);
        Assert.Equal(25060u, builder.Port);
        Assert.Equal(MySqlConnector.MySqlSslMode.Required, builder.SslMode);
    }

    [Fact]
    public void Resolve_accepts_database_connection_string()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Server=127.0.0.1;Port=3306;Database=trader;User ID=root;Password=local;",
            })
            .Build();

        var cs = MySqlConnectionStringResolver.Resolve(config);

        Assert.NotNull(cs);
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(cs);
        Assert.Equal("127.0.0.1", builder.Server);
        Assert.Equal("trader", builder.Database);
    }

    [Fact]
    public void Resolve_accepts_db_username_alias()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_HOST"] = "mysql",
                ["DB_NAME"] = "trader",
                ["DB_USERNAME"] = "trader",
                ["DB_PASSWORD"] = "trader",
            })
            .Build();

        var cs = MySqlConnectionStringResolver.Resolve(config);

        Assert.NotNull(cs);
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(cs);
        Assert.Equal("mysql", builder.Server);
        Assert.Equal("trader", builder.UserID);
    }
}
