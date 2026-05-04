using DotNetEnv;
using Microsoft.Extensions.Configuration;

namespace Trader.Api.Hosting;

/// <summary>
/// Merges optional <c>.env</c> files into configuration in <b>Development</b> only.
/// Production/staging rely on real environment variables (App Platform, Kubernetes, etc.); committed
/// <c>.env.production</c> templates are not loaded in those environments.
/// Skipped for <c>IntegrationTesting</c> so test hosts stay deterministic.
/// </summary>
public static class DotEnvBootstrap
{
    public static void Apply(ConfigurationManager configuration, IHostEnvironment env)
    {
        if (env.IsEnvironment("IntegrationTesting"))
            return;

        if (!env.IsDevelopment())
            return;

        // Only Development loads .env files. Production/Staging/etc. must use real process environment variables and appsettings*.
        var slug = env.EnvironmentName.ToLowerInvariant().Replace(" ", string.Empty);
        var roots = new List<string> { env.ContentRootPath, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        var apiRoot = FindApiProjectDirectory();
        if (apiRoot != null)
            roots.Insert(0, apiRoot);

        var paths = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in new[]
                     {
                         Path.Combine(root, ".env"),
                         Path.Combine(root, $".env.{slug}"),
                         Path.Combine(root, $".env.{slug}.local"),
                     })
            {
                if (File.Exists(file))
                    paths.Add(file);
            }
        }

        if (paths.Count == 0)
            return;

        var ordered = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var opts = new LoadOptions(setEnvVars: false, clobberExistingVars: true, onlyExactPath: true);
        foreach (var path in ordered)
        {
            foreach (var kv in DotNetEnv.Env.Load(path, opts))
                merged[kv.Key] = kv.Value;
        }

        // .env keys use "__" like environment variables; map to configuration hierarchy (see EnvironmentVariablesConfigurationProvider).
        // Skip empty values in merged files.
        configuration.AddInMemoryCollection(
            merged
                .Where(static kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(static kv =>
                    new KeyValuePair<string, string?>(
                        kv.Key.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal),
                        kv.Value)));
    }

    private static string? FindApiProjectDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (var i = 0; i < 12 && dir != null; i++)
            {
                var nested = Path.Combine(dir.FullName, "src", "Trader.Api", "Trader.Api.csproj");
                if (File.Exists(nested))
                    return Path.GetDirectoryName(nested)!;

                nested = Path.Combine(dir.FullName, "backend", "src", "Trader.Api", "Trader.Api.csproj");
                if (File.Exists(nested))
                    return Path.GetDirectoryName(nested)!;

                var flat = Path.Combine(dir.FullName, "Trader.Api.csproj");
                if (File.Exists(flat))
                    return dir.FullName;

                dir = dir.Parent;
            }
        }

        return null;
    }
}
