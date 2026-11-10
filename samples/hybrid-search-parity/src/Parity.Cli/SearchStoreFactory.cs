using GuideAntsApi.BackgroundJobs.Services.Search;
using GuideAntsApi.DataModel.Database;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Settings;
using Parity.Postgres;
using Parity.Production;
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

        if (string.Equals(profile.Searcher, SearcherKinds.Production, StringComparison.OrdinalIgnoreCase))
        {
            return CreateProduction(settings, profileName, profile);
        }

        if (!string.Equals(profile.Searcher, SearcherKinds.Copy, StringComparison.OrdinalIgnoreCase))
        {
            throw new ParityConfigurationException(
                $"Profile '{profileName}' has unknown Searcher '{profile.Searcher}'. Valid searchers: {SearcherKinds.Copy}, {SearcherKinds.Production}.");
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

    /// <summary>
    /// GuideAnts' real searchers on a schema built by the real migrations. The diagnostics query the same tables as
    /// production; on Postgres they parse text with production's configuration and normalizer, which come from the
    /// production code rather than from settings, so they can't drift from what the searcher runs.
    /// </summary>
    private static SearchEngine CreateProduction(ParitySettings settings, string profileName, ProfileSettings profile)
    {
        if (profile.TextSearchConfig is not null)
        {
            throw new ParityConfigurationException(
                $"Profile '{profileName}' sets TextSearchConfig, but the production searcher always uses '{PostgresFullText.Config}'. Remove the setting.");
        }

        switch (profile.Provider)
        {
            case "SqlServer":
                var sqlStore = new ProductionSearchStore(DatabaseProvider.SqlServer, profile.ConnectionString, profileName, TimeSpan.FromSeconds(settings.Parity.FullTextPopulationTimeoutSeconds));
                return new SearchEngine(profileName, "SqlServer", sqlStore, new SqlServerSearchDiagnostics(profileName, profile.ConnectionString), sqlStore);
            case "Postgres":
                var pgStore = new ProductionSearchStore(DatabaseProvider.Postgres, profile.ConnectionString, profileName);
                var diagnostics = new PostgresSearchDiagnostics(profileName, profile.ConnectionString, PostgresFullText.Config, PostgresFullText.NormalizeFunction);
                return new SearchEngine(profileName, "Postgres", pgStore, diagnostics, new Disposables(pgStore, diagnostics));
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
