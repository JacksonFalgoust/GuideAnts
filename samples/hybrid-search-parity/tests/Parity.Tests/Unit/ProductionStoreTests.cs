using GuideAntsApi.DataModel.Database;
using Parity.Cli;
using Parity.Core;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Production;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

/// <summary>The production store's guards and the factory's production profiles. None of these connects.</summary>
public sealed class ProductionStoreTests
{
    private const string LocalPostgres = "Host=localhost;Port=54329;Database=parity_production;Username=postgres;Password=parity";

    [Fact]
    public async Task Refuses_the_quirk_fixed_variant_production_does_not_have()
    {
        await using var store = new ProductionSearchStore(DatabaseProvider.Postgres, LocalPostgres, "prod-postgres");
        var error = await Assert.ThrowsAsync<UnsupportedQueryException>(() =>
            store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1, filterBeforeTopN: true), CancellationToken.None));
        Assert.Contains("no quirk-fixed variant", error.Message);
    }

    [Fact]
    public async Task Refuses_a_candidate_multiplier_other_than_productions()
    {
        await using var store = new ProductionSearchStore(DatabaseProvider.SqlServer, "Server=localhost,14333;Database=parity_production", "prod-sqlserver");
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1) with { CandidateMultiplier = 3 };
        var error = await Assert.ThrowsAsync<ParityConfigurationException>(() => store.HybridSearchAsync(query, CancellationToken.None));
        Assert.Contains("production's searchers always use 2", error.Message);
    }

    [Theory]
    [InlineData("SqlServer", "Server=db.example.com,1433;Database=guideants")]
    [InlineData("Postgres", "Host=db.example.com;Database=guideants")]
    public async Task Refuses_to_drop_a_database_that_is_neither_local_nor_parity_named(string provider, string connectionString)
    {
        await using var store = new ProductionSearchStore(Enum.Parse<DatabaseProvider>(provider), connectionString, "remote");
        var error = await Assert.ThrowsAsync<ParityConfigurationException>(() => store.EnsureSchemaAsync(CancellationToken.None));
        Assert.Contains("Refusing to seed", error.Message);
    }

    /// <summary>The store refuses any other multiplier, so this pins the value it compares against to the sources.</summary>
    [Fact]
    public void Candidate_multiplier_matches_both_production_searchers()
    {
        var search = Path.Combine(RepoPaths.FindRepoRoot(), "src", "server", "GuideAntsApi.BackgroundJobs", "Services", "Search");
        var sqlServer = File.ReadAllText(Path.Combine(search, "HybridSearcher.cs"));
        var postgres = File.ReadAllText(Path.Combine(search, "PostgresHybridSearcher.cs"));

        Assert.Contains($"var candidateMultiplier = {ProductionSearchStore.ProductionCandidateMultiplier};", sqlServer);
        Assert.Contains("var candidateLimit = maxResults * candidateMultiplier;", sqlServer);
        Assert.Contains($"private const int CandidateMultiplier = {ProductionSearchStore.ProductionCandidateMultiplier};", postgres);
        Assert.Contains("maxResults * CandidateMultiplier", postgres);
        Assert.Equal(ProductionSearchStore.ProductionCandidateMultiplier, new HybridSettings().CandidateMultiplier);
    }

    [Fact]
    public async Task Factory_builds_production_profiles_without_connecting()
    {
        var settings = new ParitySettings();
        settings.Profiles["ps"] = new ProfileSettings { Provider = "SqlServer", Searcher = "Production", ConnectionString = "Server=localhost,14333;Database=parity_production" };
        settings.Profiles["pp"] = new ProfileSettings { Provider = "Postgres", Searcher = "production", ConnectionString = LocalPostgres };

        await using var sqlServer = SearchStoreFactory.Create(settings, "ps");
        await using var postgres = SearchStoreFactory.Create(settings, "pp");
        Assert.IsType<ProductionSearchStore>(sqlServer.Store);
        Assert.IsType<ProductionSearchStore>(postgres.Store);
        Assert.Equal(("SqlServer", "Postgres"), (sqlServer.Provider, postgres.Provider));
    }

    [Fact]
    public void Factory_rejects_an_unknown_searcher_and_a_text_search_config_on_production_profiles()
    {
        var settings = new ParitySettings();
        settings.Profiles["odd"] = new ProfileSettings { Provider = "Postgres", Searcher = "Elastic", ConnectionString = LocalPostgres };
        settings.Profiles["cfg"] = new ProfileSettings { Provider = "Postgres", Searcher = "Production", ConnectionString = LocalPostgres, TextSearchConfig = "english" };

        Assert.Contains("Valid searchers: Copy, Production", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "odd")).Message);
        Assert.Contains("always uses 'guideants_english_nostem'", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "cfg")).Message);
    }

    [Fact]
    public void Committed_production_profiles_use_their_own_databases()
    {
        var settings = AppConfig.Load(RepoPaths.FindSampleRoot(), includeLocalOverrides: false, environment: _ => null);
        Assert.Equal(("SqlServer", "Production"), (settings.Profiles["prod-sqlserver"].Provider, settings.Profiles["prod-sqlserver"].Searcher));
        Assert.Equal(("Postgres", "Production"), (settings.Profiles["prod-postgres"].Provider, settings.Profiles["prod-postgres"].Searcher));
        Assert.Contains("Database=parity_production", settings.Profiles["prod-sqlserver"].ConnectionString);
        Assert.Contains("Database=parity_production", settings.Profiles["prod-postgres"].ConnectionString);
    }
}
