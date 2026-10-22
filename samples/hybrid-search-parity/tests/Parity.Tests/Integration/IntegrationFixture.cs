using Microsoft.Data.SqlClient;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.SqlServer;
using Parity.Tests.Support;
using Testcontainers.MsSql;

namespace Parity.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "databases";
}

/// <summary>
/// Starts the containers once per test run. Databases are seeded lazily, once per key. The SQL
/// Server image is amd64-only, so on Apple Silicon it runs emulated and takes a few minutes to start.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public static readonly TimeSpan PopulationTimeout = TimeSpan.FromMinutes(10);

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage(new ImageSettings().SqlServer)
        .WithPassword("Parity_Passw0rd!")
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _seeded = new();

    public Task InitializeAsync() => _sqlServer.StartAsync();

    public async Task DisposeAsync() => await _sqlServer.DisposeAsync();

    public string SqlServerConnectionString(string database) =>
        new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            Encrypt = false,
        }.ConnectionString;

    public Task<string> MiniSqlServerAsync() => SeedOnceAsync(
        "mini-sqlserver",
        () => SqlServerConnectionString("parity_mini"),
        cs => new SqlServerSearchStore("mini-sqlserver", cs, PopulationTimeout),
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
