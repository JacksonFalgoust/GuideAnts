using System.Text.RegularExpressions;
using Parity.Cli;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public sealed class CliCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-cmd-{Guid.NewGuid():N}");
    private readonly Dictionary<string, InMemoryEngine> _engines = new();

    public CliCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inputs"));
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), """
            { "Profiles": { "a": { "Provider": "InMemory", "ConnectionString": "x" }, "b": { "Provider": "InMemory", "ConnectionString": "x" } },
              "Parity": { "Baseline": "a", "Candidate": "b" } }
            """);
        File.WriteAllText(Path.Combine(_root, DataStore.EdgeCasesFile), "[]");
        File.WriteAllText(Path.Combine(_root, DataStore.QueriesFile), """
            [ { "id": "Q01", "group": "exact", "text": "conversation lock" }, { "id": "Q37", "group": "stress", "text": "alpha beta" } ]
            """);
        var queries = DataStore.ReadQueries(_root);
        DataStore.WriteAtomically(_root, new GeneratedData("test0000", DateTimeOffset.UtcNow, "fake-hash", MiniCorpus.Chunks, queries,
            queries.Select(q => HashEmbedding.Create(q.Text)).ToList(), new Dictionary<string, IReadOnlyList<Guid>>(), new Dictionary<string, string>()), CancellationToken.None);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (CliApp App, StringWriter Out, StringWriter Err) App(Func<string, InMemoryEngine>? create = null, IDictionary<string, string?>? overrides = null, bool realEngines = false, Func<string, InMemoryEngine, ISearchDiagnostics>? diagnostics = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Func<ParitySettings, string, SearchEngine>? factory = realEngines ? null : (_, name) =>
        {
            if (!_engines.TryGetValue(name, out var engine))
            {
                engine = _engines[name] = create?.Invoke(name) ?? new InMemoryEngine();
            }

            return new SearchEngine(name, "InMemory", engine, diagnostics?.Invoke(name, engine) ?? engine);
        };
        var app = new CliApp(output, error, _root, embedderFactory: _ => null, configOverrides: overrides, engineFactory: factory, probe: new FixedProbe(), includeLocalOverrides: false);
        return (app, output, error);
    }

    private sealed class FixedProbe : IEnvironmentProbe
    {
        public Task<string> ImageDigestAsync(string image, CancellationToken ct) => Task.FromResult("sha256:test");

        public Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct) => Task.FromResult(new GitState("abcd1234", false));
    }

    [Fact]
    public async Task SeedThenSearchPrintsRankedHitsForASavedQuery()
    {
        var (app, output, _) = App();
        Assert.Equal(ExitCodes.Success, await app.RunAsync(["seed", "--all"], CancellationToken.None));
        Assert.Equal(ExitCodes.Success, await app.RunAsync(["search", "conversation", "lock", "--profile", "a"], CancellationToken.None));
        Assert.Contains("Seeding a", output.ToString());
        Assert.Contains("saved Q01", output.ToString());
        Assert.Contains("mini/m01", output.ToString());
    }

    [Fact]
    public async Task CompareWritesResultsAndExitsZeroWhenTheEnginesAgree()
    {
        var (app, output, _) = App();
        await app.RunAsync(["seed", "--all"], CancellationToken.None);
        var outPath = Path.Combine(_root, "RESULTS.md");

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["compare", "--out", outPath], CancellationToken.None));
        Assert.Contains("**Verdict: SWITCHABLE**", File.ReadAllText(outPath));
        Assert.Contains("Hybrid search parity: SWITCHABLE", output.ToString());
        Assert.Contains("known-differences.json: SHA-256 `absent`", File.ReadAllText(outPath));
        Assert.DoesNotContain("effective settings differ", File.ReadAllText(outPath));
    }

    [Fact]
    public async Task CompareWarnsWhenTheEffectiveSettingsDifferFromAppsettings()
    {
        var (app, _, _) = App(overrides: new Dictionary<string, string?> { ["Parity:VectorTopK"] = "5" });
        await app.RunAsync(["seed", "--all"], CancellationToken.None);
        var outPath = Path.Combine(_root, "RESULTS.md");
        await app.RunAsync(["compare", "--out", outPath], CancellationToken.None);
        Assert.Contains("> - Parity:VectorTopK: committed 10, effective 5", File.ReadAllText(outPath));
    }

    [Fact]
    public async Task CompareExitsOneWhenNotSwitchable()
    {
        var (app, output, _) = App(name => name == "b"
            ? new InMemoryEngine { TextRank = (c, q) => Regex.IsMatch(c, "conversation locks?", RegexOptions.IgnoreCase) && q == "conversation lock" ? 0.5 : InMemoryEngine.DefaultTextRank(c, q) }
            : new InMemoryEngine());
        await app.RunAsync(["seed", "--all"], CancellationToken.None);

        Assert.Equal(ExitCodes.NotSwitchable, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains("NOT SWITCHABLE", output.ToString());
        Assert.Contains("full-text  UNREVIEWED", output.ToString());
    }

    private sealed class PartialCoverage(InMemoryEngine inner) : ISearchDiagnostics
    {
        public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) => inner.VectorSearchAsync(vector, scope, k, ct);

        public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) => inner.TextMatchAsync(text, scope, ct);

        public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct) => inner.ScoreChunksAsync(query, chunkIds, ct);

        public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) => inner.TokenizeAsync(text, ct);

        public Task<EngineInfo> DescribeAsync(CancellationToken ct) => inner.DescribeAsync(ct);

        public async Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct)
        {
            var real = await inner.GetIndexCoverageAsync(ct);
            return new IndexCoverage(real.Rows, real.Rows - 1);
        }
    }

    [Fact]
    public async Task CompareRefusesAHalfIndexedPrimaryProfile()
    {
        var (app, _, error) = App(diagnostics: (name, engine) => name == "b" ? new PartialCoverage(engine) : engine);
        await app.RunAsync(["seed", "--all"], CancellationToken.None);

        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains($"Profile 'b' has {MiniCorpus.Chunks.Count - 1} of {MiniCorpus.Chunks.Count} rows full-text indexed", error.ToString());
        Assert.Contains("seed --profile b", error.ToString());
    }

    [Fact]
    public async Task HalfIndexedInformationalProfileIsNotAvailableAndKeepsTheVerdict()
    {
        var (app, output, _) = App(
            overrides: new Dictionary<string, string?>
            {
                ["Profiles:c:Provider"] = "InMemory",
                ["Profiles:c:ConnectionString"] = "x",
                ["Parity:Informational:0"] = "c",
            },
            diagnostics: (name, engine) => name == "c" ? new PartialCoverage(engine) : engine);
        await app.RunAsync(["seed", "--all"], CancellationToken.None);

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains("SWITCHABLE", output.ToString());
        Assert.Contains($"c: not available (Profile 'c' has {MiniCorpus.Chunks.Count - 1} of {MiniCorpus.Chunks.Count} rows full-text indexed", output.ToString());
    }

    [Fact]
    public async Task CompareRefusesAnUnseededProfile()
    {
        var (app, _, error) = App();
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains("seed --profile a", error.ToString());
    }

    [Fact]
    public async Task UnreachableDatabaseExitsTwoNamingProfileAndHost()
    {
        var (app, _, error) = App(overrides: new Dictionary<string, string?>
        {
            ["Profiles:sql:Provider"] = "SqlServer",
            ["Profiles:sql:ConnectionString"] = "Server=127.0.0.1,1;Database=parity;User ID=sa;Password=x;TrustServerCertificate=True",
        }, realEngines: true);

        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["search", "conversation lock", "--profile", "sql"], CancellationToken.None));
        Assert.Contains("Can't reach profile 'sql' (SqlServer) at 127.0.0.1,1", error.ToString());
    }

    [Fact]
    public async Task UnsavedTextNeedsAKey()
    {
        var (app, _, error) = App();
        await app.RunAsync(["seed", "--all"], CancellationToken.None);
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["compare", "--query", "something new"], CancellationToken.None));
        Assert.Contains("OPENAI_API_KEY", error.ToString());
    }

    [Fact]
    public async Task SavedQueriesMatchAfterTrimmingButNotAcrossCase()
    {
        var data = DataStore.Load(_root);
        var embedder = new FakeEmbeddingClient();
        Assert.Equal("saved Q01", (await QueryVectors.ResolveAsync("  conversation lock ", data, embedder, CancellationToken.None)).Source);
        Assert.Equal(0, embedder.Calls);
        Assert.Equal("live embedding", (await QueryVectors.ResolveAsync("Conversation Lock", data, embedder, CancellationToken.None)).Source);
        Assert.Equal(1, embedder.Calls);
    }

    [Fact]
    public void FactoryRejectsUnknownProfilesAndProviders()
    {
        var settings = new ParitySettings();
        settings.Profiles["sqlserver"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = "Server=x" };
        settings.Profiles["odd"] = new ProfileSettings { Provider = "Oracle", ConnectionString = "x" };

        Assert.Contains("Configured profiles: odd, sqlserver", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "nope")).Message);
        Assert.Contains("Valid providers: SqlServer, Postgres", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "odd")).Message);
    }

    [Fact]
    public async Task FactoryBuildsEachProviderWithoutConnecting()
    {
        var settings = new ParitySettings();
        settings.Profiles["s"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = "Server=localhost,14333;Database=parity" };
        settings.Profiles["p"] = new ProfileSettings { Provider = "Postgres", ConnectionString = "Host=localhost;Port=54329;Database=parity", TextSearchConfig = "english" };

        await using var s = SearchStoreFactory.Create(settings, "s");
        await using var p = SearchStoreFactory.Create(settings, "p");
        Assert.Equal(("SqlServer", "Postgres"), (s.Provider, p.Provider));
    }

    [Fact]
    public async Task MissingExecutablesReportUnknown()
    {
        var probe = new ProcessEnvironmentProbe("definitely-not-docker-xyz", "definitely-not-git-xyz");
        Assert.StartsWith("unknown", await probe.ImageDigestAsync("pgvector/pgvector:0.8.7-pg18", CancellationToken.None));
        Assert.Equal(new GitState("unknown", false), await probe.GitStateAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task GitStateReadsTheRealRepository()
    {
        var state = await new ProcessEnvironmentProbe().GitStateAsync(RepoPaths.FindSampleRoot(), CancellationToken.None);
        Assert.Matches("^[0-9a-f]{8}$", state.Commit);
    }
}
