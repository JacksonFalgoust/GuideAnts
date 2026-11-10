using System.Globalization;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Model;
using Parity.Core.Reporting;
using Parity.Core.Settings;

namespace Parity.Cli.Commands;

public sealed class CompareCommand(
    TextWriter output,
    string sampleRoot,
    Func<ParitySettings, string, SearchEngine> engineFactory,
    IEmbeddingClient? embedder,
    IEnvironmentProbe probe)
{
    public const string KnownDifferencesFile = "known-differences.json";

    public async Task<int> RunAsync(CliArgs args, ParitySettings settings, CancellationToken ct)
    {
        ChooseProfiles(args, settings);
        var data = DataStore.Load(sampleRoot);
        if (args.Get("--query") is { } live)
        {
            return await RunLiveAsync(live, settings, data, ct);
        }

        RequireDistinctProfiles(settings);
        await using var baseline = engineFactory(settings, settings.Parity.Baseline);
        await using var candidate = engineFactory(settings, settings.Parity.Candidate);
        var baselineInfo = await EnsureSeededAsync(baseline, data, ct);
        var candidateInfo = await EnsureSeededAsync(candidate, data, ct);
        var known = KnownDifferences.ForCandidate(KnownDifferences.Load(Path.Combine(sampleRoot, KnownDifferencesFile)), candidate.Profile);
        var progress = new InlineProgress(message => output.WriteLine($"  {message}"));

        output.WriteLine($"Comparing {baseline.Profile} (baseline) with {candidate.Profile} (candidate)...");
        var pairs = await new ComparisonRunner(baseline, candidate, data, settings, known).RunAsync(ParityScopes.All, ct, progress: progress);

        output.WriteLine("Informational: quirk fixed on both engines...");
        const string quirkLabel = "Quirk fixed on both engines (not production)";
        InformationalSummary quirk;
        try
        {
            var quirkPairs = await new ComparisonRunner(baseline, candidate, data, settings, []).RunAsync(ParityScopes.All, ct, filterBeforeTopN: true);
            quirk = InformationalSummary.From(quirkLabel, quirkPairs, withMatchCounts: false);
        }
        catch (UnsupportedQueryException ex)
        {
            quirk = InformationalSummary.Unavailable(quirkLabel, ex.Message);
        }

        var profiles = new List<InformationalSummary>();
        foreach (var name in settings.Parity.Informational)
        {
            output.WriteLine($"Informational: {name}...");
            profiles.Add(await InformationalProfileAsync(name, InformationalLabel(settings, name), baseline, data, settings, ct));
        }

        var verdict = Verdict.Evaluate(pairs, settings.Parity.Thresholds);
        var git = await probe.GitStateAsync(sampleRoot, ct);
        var codeUnderTest = UsesProductionSearch(settings) ? await probe.CodeUnderTestAsync(sampleRoot, ct) : null;
        var context = new RunContext(
            DateTimeOffset.UtcNow, git.Commit, git.SampleDirty, baseline.Profile, candidate.Profile, baselineInfo, candidateInfo,
            data.Manifest, settings.Parity.Thresholds, settings.Parity.VectorTolerance, settings.Parity.TieTolerance,
            settings.Images.SqlServer, await probe.ImageDigestAsync(settings.Images.SqlServer, ct),
            settings.Images.Postgres, await probe.ImageDigestAsync(settings.Images.Postgres, ct),
            KnownDifferencesHash(), SettingsDrift.Differences(CommittedSettings(args), settings), codeUnderTest);
        var labels = data.Chunks.ToDictionary(c => c.Id, c => c.Kind == ChunkKind.Edge ? c.SourcePath : $"{c.SourcePath}#{c.ChunkIndex}");
        var report = new ComparisonReport(context, pairs, verdict, quirk, profiles, KnownDifferences.FindStale(known, pairs), labels);

        output.Write(ConsoleSummary.Render(report));
        if (args.Get("--out") is { } outPath)
        {
            File.WriteAllText(outPath, MarkdownReport.Render(report));
            output.WriteLine($"Wrote {outPath}");
        }

        return verdict.Switchable ? ExitCodes.Success : ExitCodes.NotSwitchable;
    }

    /// <summary>--baseline and --candidate pick the profiles for this run instead of Parity:Baseline and Parity:Candidate.</summary>
    private static void ChooseProfiles(CliArgs args, ParitySettings settings)
    {
        settings.Parity.Baseline = args.Get("--baseline") ?? settings.Parity.Baseline;
        settings.Parity.Candidate = args.Get("--candidate") ?? settings.Parity.Candidate;
    }

    /// <summary>appsettings.json alone, with this run's profile choice: a profile picked on the command line isn't drift.</summary>
    private ParitySettings CommittedSettings(CliArgs args)
    {
        var committed = AppConfig.Load(sampleRoot, includeLocalOverrides: false, environment: _ => null);
        ChooseProfiles(args, committed);
        return committed;
    }

    /// <summary>Comparing an engine with itself would "pass" trivially, so refuse the same profile or the same database.</summary>
    private static void RequireDistinctProfiles(ParitySettings settings)
    {
        var (baseline, candidate) = (settings.Parity.Baseline, settings.Parity.Candidate);
        if (string.Equals(baseline, candidate, StringComparison.OrdinalIgnoreCase))
        {
            throw new ParityConfigurationException($"Parity:Baseline and Parity:Candidate are both '{baseline}'. Comparing a profile with itself proves nothing; point them at different profiles.");
        }

        if (settings.Profiles.TryGetValue(baseline, out var a) && settings.Profiles.TryGetValue(candidate, out var b) &&
            string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ConnectionString.Trim(), b.ConnectionString.Trim(), StringComparison.Ordinal))
        {
            throw new ParityConfigurationException(
                $"Profiles '{baseline}' and '{candidate}' use the same provider ({a.Provider}) and connection string, so they are the same database. Comparing a database with itself proves nothing.");
        }
    }

    private string KnownDifferencesHash()
    {
        var path = Path.Combine(sampleRoot, KnownDifferencesFile);
        return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : "absent";
    }

    private async Task<InformationalSummary> InformationalProfileAsync(string name, string label, SearchEngine baseline, DataSet data, ParitySettings settings, CancellationToken ct)
    {
        try
        {
            await using var engine = engineFactory(settings, name);
            await EnsureSeededAsync(engine, data, ct);
            var pairs = await new ComparisonRunner(baseline, engine, data, settings, []).RunAsync(ParityScopes.All, ct);
            return InformationalSummary.From(label, pairs);
        }
        catch (Exception ex) when (ex is NotSeededException or EngineUnavailableException or ParityConfigurationException)
        {
            return InformationalSummary.Unavailable(label, ex.Message);
        }
    }

    private static bool IsProduction(ParitySettings settings, string profile) =>
        settings.Profiles.TryGetValue(profile, out var p) && string.Equals(p.Searcher, SearcherKinds.Production, StringComparison.OrdinalIgnoreCase);

    /// <summary>The baseline or the candidate runs production's searchers, so the report must cover that code too.</summary>
    private static bool UsesProductionSearch(ParitySettings settings) =>
        IsProduction(settings, settings.Parity.Baseline) || IsProduction(settings, settings.Parity.Candidate);

    /// <summary>In a production comparison, says when an informational row is one of the sample's own stores.</summary>
    private static string InformationalLabel(ParitySettings settings, string name) =>
        UsesProductionSearch(settings) && !IsProduction(settings, name) && settings.Profiles.TryGetValue(name, out var profile)
            ? $"{name} (the sample's own {profile.Provider} store, not production search)"
            : name;

    private async Task<int> RunLiveAsync(string text, ParitySettings settings, DataSet data, CancellationToken ct)
    {
        await using var baseline = engineFactory(settings, settings.Parity.Baseline);
        await using var candidate = engineFactory(settings, settings.Parity.Candidate);
        await EnsureSeededAsync(baseline, data, ct);
        await EnsureSeededAsync(candidate, data, ct);
        var (vector, source) = await QueryVectors.ResolveAsync(text, data, embedder, ct);
        var h = settings.Hybrid;

        output.WriteLine($"Live comparison of \"{text.Trim()}\" ({source}). Not part of any verdict.");
        output.WriteLine($"  {baseline.Profile} | {candidate.Profile}");
        foreach (var scope in ParityScopes.All)
        {
            var query = new HybridQuery(text.Trim(), vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier);
            var a = await baseline.Store.HybridSearchAsync(query, ct);
            var b = await candidate.Store.HybridSearchAsync(query, ct);
            output.WriteLine($"Scope {scope.Label}");
            for (var i = 0; i < h.MaxResults; i++)
            {
                output.WriteLine($"  {i + 1}. {Describe(a, i, data)} | {Describe(b, i, data)}");
            }
        }

        return ExitCodes.Success;
    }

    private static async Task<EngineInfo> EnsureSeededAsync(SearchEngine engine, DataSet data, CancellationToken ct)
    {
        await engine.Store.PingServerAsync(ct);
        var coverage = await engine.Diagnostics.GetIndexCoverageAsync(ct); // throws NotSeededException when the database or table is missing
        if (coverage.Rows != data.Chunks.Count)
        {
            throw new NotSeededException(
                $"Profile '{engine.Profile}' has {coverage.Rows} chunks but data/ has {data.Chunks.Count}. Run `seed --profile {engine.Profile}`.");
        }

        if (coverage.IndexedRows != coverage.Rows)
        {
            throw new NotSeededException(
                $"Profile '{engine.Profile}' has {coverage.IndexedRows} of {coverage.Rows} rows full-text indexed (index incomplete or still populating). Re-run `seed --profile {engine.Profile}`.");
        }

        return await engine.Diagnostics.DescribeAsync(ct);
    }

    private static string Describe(EngineResult<IReadOnlyList<SearchHit>> result, int index, DataSet data)
    {
        if (!result.IsSuccess)
        {
            return $"error {result.Error.Code}";
        }

        if (index >= result.Value.Count)
        {
            return "-";
        }

        var hit = result.Value[index];
        var chunk = data.FindChunk(hit.ChunkId);
        var label = chunk is null ? hit.ChunkId.ToString()[..8] : $"{chunk.SourcePath}{(chunk.Kind == ChunkKind.Doc ? $"#{chunk.ChunkIndex}" : "")}";
        return string.Create(CultureInfo.InvariantCulture, $"{label} {hit.Score:F4}");
    }

    /// <summary>Reports synchronously, so progress lines stay in order with the rest of the output.</summary>
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
