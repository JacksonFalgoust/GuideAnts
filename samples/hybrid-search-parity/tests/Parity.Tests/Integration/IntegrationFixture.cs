using Microsoft.Data.SqlClient;
using Npgsql;
using Parity.Cli;
using Parity.Cli.Commands;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;
using GuideAntsApi.DataModel.Database;
using Parity.Postgres;
using Parity.Production;
using Parity.SqlServer;
using Parity.Tests.Support;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Parity.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "databases";
}

/// <summary>
/// Starts both containers (one after the other) once per test run. Databases are seeded lazily, once per key. The SQL
/// Server image is amd64-only, so on Apple Silicon it runs emulated and takes a few minutes to start.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public static readonly TimeSpan PopulationTimeout = TimeSpan.FromMinutes(10);

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage(new ImageSettings().SqlServer)
        .WithPassword("Parity_Passw0rd!")
        .Build();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage(new ImageSettings().Postgres)
        .WithPassword("parity")
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _seeded = new();

    private readonly SemaphoreSlim _comparisonGate = new(1, 1);
    private IReadOnlyList<PairResult>? _fullPairs;
    private IReadOnlyList<PairResult>? _productionPairs;

    public Lazy<DataSet> FullData { get; } = new(() => DataStore.Load(RepoPaths.FindSampleRoot()));

    /// <summary>Both engines seeded with the committed data, created through the same factory the CLI uses.</summary>
    public async Task<(SearchEngine Baseline, SearchEngine Candidate)> FullEnginesAsync()
    {
        var data = FullData.Value;
        var createdUtc = data.Manifest.GeneratedUtc.UtcDateTime;
        var sqlCs = await SeedOnceAsync("full-sqlserver", () => SqlServerConnectionString("parity"),
            cs => new SqlServerSearchStore("sqlserver", cs, PopulationTimeout), data.Chunks, createdUtc);
        var pgCs = await SeedOnceAsync("full-postgres", () => PostgresConnectionString("parity"),
            cs => new PostgresSearchStore("postgres", cs, PostgresSchema.CustomConfigName), data.Chunks, createdUtc);

        var settings = new ParitySettings();
        settings.Profiles["sqlserver"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = sqlCs };
        settings.Profiles["postgres"] = new ProfileSettings { Provider = "Postgres", ConnectionString = pgCs, TextSearchConfig = PostgresSchema.CustomConfigName };
        return (SearchStoreFactory.Create(settings, "sqlserver"), SearchStoreFactory.Create(settings, "postgres"));
    }

    /// <summary>
    /// Both servers seeded with the committed data through the production schema (the real migrations), with engines
    /// created by the CLI's factory for the <c>Production</c> searcher.
    /// </summary>
    public async Task<(SearchEngine SqlServer, SearchEngine Postgres)> ProductionEnginesAsync()
    {
        var data = FullData.Value;
        var createdUtc = data.Manifest.GeneratedUtc.UtcDateTime;
        var sqlCs = await SeedOnceAsync("prod-sqlserver", () => SqlServerConnectionString("parity_production"),
            cs => new ProductionSearchStore(DatabaseProvider.SqlServer, cs, "prod-sqlserver", PopulationTimeout), data.Chunks, createdUtc);
        var pgCs = await SeedOnceAsync("prod-postgres", () => PostgresConnectionString("parity_production"),
            cs => new ProductionSearchStore(DatabaseProvider.Postgres, cs, "prod-postgres"), data.Chunks, createdUtc);

        var settings = new ParitySettings();
        settings.Profiles["prod-sqlserver"] = new ProfileSettings { Provider = "SqlServer", Searcher = "Production", ConnectionString = sqlCs };
        settings.Profiles["prod-postgres"] = new ProfileSettings { Provider = "Postgres", Searcher = "Production", ConnectionString = pgCs };
        return (SearchStoreFactory.Create(settings, "prod-sqlserver"), SearchStoreFactory.Create(settings, "prod-postgres"));
    }

    /// <summary>The verdict comparison (production behaviour, saved queries, all four scopes), computed once.</summary>
    public async Task<IReadOnlyList<PairResult>> FullComparisonAsync()
    {
        await _comparisonGate.WaitAsync();
        try
        {
            if (_fullPairs is not null)
            {
                return _fullPairs;
            }

            var (baseline, candidate) = await FullEnginesAsync();
            await using (baseline)
            await using (candidate)
            {
                var known = KnownDifferences.ForCandidate(
                    KnownDifferences.Load(Path.Combine(RepoPaths.FindSampleRoot(), CompareCommand.KnownDifferencesFile)), candidate.Profile);
                _fullPairs = await new ComparisonRunner(baseline, candidate, FullData.Value, new ParitySettings(), known)
                    .RunAsync(ParityScopes.All, CancellationToken.None);
            }

            return _fullPairs;
        }
        finally
        {
            _comparisonGate.Release();
        }
    }

    /// <summary>The production verdict comparison (prod-sqlserver vs prod-postgres, saved queries, all four scopes), computed once.</summary>
    public async Task<IReadOnlyList<PairResult>> ProductionComparisonAsync()
    {
        await _comparisonGate.WaitAsync();
        try
        {
            if (_productionPairs is not null)
            {
                return _productionPairs;
            }

            var (baseline, candidate) = await ProductionEnginesAsync();
            await using (baseline)
            await using (candidate)
            {
                var known = KnownDifferences.ForCandidate(
                    KnownDifferences.Load(Path.Combine(RepoPaths.FindSampleRoot(), CompareCommand.KnownDifferencesFile)), candidate.Profile);
                _productionPairs = await new ComparisonRunner(baseline, candidate, FullData.Value, new ParitySettings(), known)
                    .RunAsync(ParityScopes.All, CancellationToken.None);
            }

            return _productionPairs;
        }
        finally
        {
            _comparisonGate.Release();
        }
    }

    // Sequential on purpose: starting both at once made SQL Server time out on TCP connect under emulation.
    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        try
        {
            await _postgres.StartAsync();
        }
        catch
        {
            await _postgres.DisposeAsync();
            await _sqlServer.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public string SqlServerConnectionString(string database) =>
        new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            Encrypt = false,
        }.ConnectionString;

    public string PostgresConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public Task<string> MiniSqlServerAsync() => SeedOnceAsync(
        "mini-sqlserver",
        () => SqlServerConnectionString("parity_mini"),
        cs => new SqlServerSearchStore("mini-sqlserver", cs, PopulationTimeout),
        MiniCorpus.Chunks,
        MiniCorpus.CreatedUtc);

    public Task<string> MiniPostgresAsync(string config = PostgresSchema.CustomConfigName) => SeedOnceAsync(
        $"mini-postgres-{config}",
        () => PostgresConnectionString($"parity_mini_{config}"),
        cs => new PostgresSearchStore($"mini-postgres-{config}", cs, config),
        MiniCorpus.Chunks,
        MiniCorpus.CreatedUtc);

    /// <summary>Seeds <paramref name="chunks"/> into a fresh database the first time <paramref name="key"/> is asked for.</summary>
    public async Task<string> SeedOnceAsync(
        string key,
        Func<string> connectionString,
        Func<string, ISearchStore> createStore,
        IReadOnlyList<ChunkRecord> chunks,
        DateTime createdUtc)
    {
        await _gate.WaitAsync();
        try
        {
            if (_seeded.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var cs = connectionString();
            var store = createStore(cs);
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(chunks, createdUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            _seeded[key] = cs;
            return cs;
        }
        finally
        {
            _gate.Release();
        }
    }
}
