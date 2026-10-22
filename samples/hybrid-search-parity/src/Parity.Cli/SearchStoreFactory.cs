using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Settings;
using Parity.Postgres;
using Parity.SqlServer;

namespace Parity.Cli;

/// <summary>The one place a profile's Provider becomes a concrete implementation.</summary>
public static class SearchStoreFactory
{
    public static SearchEngine Create(ParitySettings settings, string profileName)
    {
        if (!settings.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new ParityConfigurationException(
                $"Unknown profile '{profileName}'. Configured profiles: {string.Join(", ", settings.Profiles.Keys.Order(StringComparer.Ordinal))}.");
        }

        if (string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            throw new ParityConfigurationException($"Profile '{profileName}' has no ConnectionString.");
        }

        switch (profile.Provider)
        {
            case "SqlServer":
                return new SearchEngine(
                    profileName,
                    "SqlServer",
                    new SqlServerSearchStore(profileName, profile.ConnectionString, TimeSpan.FromSeconds(settings.Parity.FullTextPopulationTimeoutSeconds)),
                    new SqlServerSearchDiagnostics(profileName, profile.ConnectionString));
            case "Postgres":
                var config = profile.TextSearchConfig ?? PostgresSchema.CustomConfigName;
                var store = new PostgresSearchStore(profileName, profile.ConnectionString, config);
                var diagnostics = new PostgresSearchDiagnostics(profileName, profile.ConnectionString, config);
                return new SearchEngine(profileName, "Postgres", store, diagnostics, new Disposables(store, diagnostics));
            default:
                throw new ParityConfigurationException(
                    $"Profile '{profileName}' has unknown Provider '{profile.Provider}'. Valid providers: SqlServer, Postgres.");
        }
    }

    private sealed class Disposables(params IAsyncDisposable[] items) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var item in items)
            {
                await item.DisposeAsync();
            }
        }
    }
}
