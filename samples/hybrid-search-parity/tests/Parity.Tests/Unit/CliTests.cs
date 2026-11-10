using Parity.Cli;
using Parity.Core;
using Parity.Core.Settings;

namespace Parity.Tests.Unit;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-cli-{Guid.NewGuid():N}");

    public CliTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ParsesCommandPositionalsFlagsAndValues()
    {
        var args = CliArgs.Parse(["search", "conversation", "lock", "--profile", "postgres", "--quirk-fixed"]);
        Assert.Equal("search", args.Command);
        Assert.Equal(new[] { "conversation", "lock" }, args.Positionals);
        Assert.Equal("postgres", args.Get("--profile"));
        Assert.True(args.Has("--quirk-fixed"));
        Assert.Null(args.Get("--scope"));
    }

    [Fact]
    public void RejectsUnknownOptionsAndMissingValues()
    {
        Assert.Contains("Unknown option --frobnicate", Assert.Throws<ParityConfigurationException>(() => CliArgs.Parse(["seed", "--frobnicate"])).Message);
        Assert.Contains("--profile needs a value", Assert.Throws<ParityConfigurationException>(() => CliArgs.Parse(["seed", "--profile"])).Message);
    }

    [Fact]
    public void CommittedAppsettingsMatchTheSpec()
    {
        var settings = AppConfig.Load(RepoPaths.FindSampleRoot(), environment: _ => null, includeLocalOverrides: false);

        Assert.Equal(new[] { "postgres", "postgres-english", "prod-postgres", "prod-sqlserver", "sqlserver" }, settings.Profiles.Keys.Order());
        Assert.Equal("SqlServer", settings.Profiles["sqlserver"].Provider);
        Assert.Equal(["Copy", "Copy", "Copy"], new[] { "sqlserver", "postgres", "postgres-english" }.Select(p => settings.Profiles[p].Searcher));
        Assert.Equal("parity_english_nostem", settings.Profiles["postgres"].TextSearchConfig);
        Assert.Equal("english", settings.Profiles["postgres-english"].TextSearchConfig);
        Assert.Equal(new[] { "postgres-english" }, settings.Parity.Informational);
        Assert.Equal("sqlserver", settings.Parity.Baseline);
        Assert.Equal("postgres", settings.Parity.Candidate);
        Assert.Equal((5, 0.7, 0.3, 2), (settings.Hybrid.MaxResults, settings.Hybrid.VectorWeight, settings.Hybrid.TextWeight, settings.Hybrid.CandidateMultiplier));
        Assert.Equal(10, settings.Parity.VectorTopK);
        Assert.Equal(1e-5, settings.Parity.VectorTolerance);
        Assert.Equal(1e-6, settings.Parity.TieTolerance);
        Assert.Equal(0.9, settings.Parity.Thresholds.MinMeanTop5Overlap);
        Assert.Equal(0.8, settings.Parity.Thresholds.MinSameTopOneRate);
        // The Testcontainers fixture uses ImageSettings defaults; keep them identical to appsettings.json.
        Assert.Equal(new ImageSettings().SqlServer, settings.Images.SqlServer);
        Assert.Equal(new ImageSettings().Postgres, settings.Images.Postgres);
    }

    [Fact]
    public void SettingsDriftListsOnlyTheDecisiveValuesThatChanged()
    {
        var committed = AppConfig.Load(RepoPaths.FindSampleRoot(), environment: _ => null, includeLocalOverrides: false);
        var effective = AppConfig.Load(RepoPaths.FindSampleRoot(), environment: _ => null, includeLocalOverrides: false);
        Assert.Empty(Parity.Core.Reporting.SettingsDrift.Differences(committed, effective));

        effective.Parity.Candidate = "postgres-english";
        effective.Parity.VectorTopK = 20;
        effective.Parity.Thresholds.MinSameTopOneRate = 0.5;
        effective.Hybrid.CandidateMultiplier = 3;
        effective.OpenAI.Model = "unrelated";
        var differences = Parity.Core.Reporting.SettingsDrift.Differences(committed, effective);
        Assert.Equal(
            ["Parity:Candidate: committed postgres, effective postgres-english", "Parity:VectorTopK: committed 10, effective 20",
             "Parity:Thresholds:MinSameTopOneRate: committed 0.8, effective 0.5", "Hybrid:CandidateMultiplier: committed 2, effective 3"],
            differences);
    }

    [Fact]
    public async Task GitStateIgnoresTheResultsReportsButNotOtherSampleChanges()
    {
        var repo = Path.Combine(_root, "repo");
        var sample = Path.Combine(repo, "sample");
        Directory.CreateDirectory(sample);
        File.WriteAllText(Path.Combine(sample, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(sample, "RESULTS.md"), "old");
        File.WriteAllText(Path.Combine(sample, "RESULTS-production.md"), "old");
        File.WriteAllText(Path.Combine(repo, "outside.txt"), "x");
        void Git(params string[] args)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false" }.Concat(args))
            {
                start.ArgumentList.Add(a);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        Git("init", "-q");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        var probe = new ProcessEnvironmentProbe();
        Assert.False((await probe.GitStateAsync(sample, CancellationToken.None)).SampleDirty);

        File.WriteAllText(Path.Combine(sample, "RESULTS.md"), "regenerated");
        File.WriteAllText(Path.Combine(sample, "RESULTS-production.md"), "regenerated");
        File.WriteAllText(Path.Combine(repo, "outside.txt"), "changed");
        Assert.False((await probe.GitStateAsync(sample, CancellationToken.None)).SampleDirty);

        File.WriteAllText(Path.Combine(sample, "known-differences.json"), "[]");
        Assert.True((await probe.GitStateAsync(sample, CancellationToken.None)).SampleDirty);
    }

    [Fact]
    public void OpenAiKeyFallsBackToTheEnvironment()
    {
        var settings = AppConfig.Load(_root, new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "" }, name => name == "OPENAI_API_KEY" ? "sk-env" : null, includeLocalOverrides: false);
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
    }

    private const string OpenAiEndpoint = "https://api.openai.com/v1";
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";

    private static Func<string, string?> Env(string? openAi, string? openRouter) =>
        name => name switch { "OPENAI_API_KEY" => openAi, "OPENROUTER_API_KEY" => openRouter, _ => null };

    [Fact]
    public void OpenRouterKeyAloneSwitchesKeyEndpointAndModel()
    {
        var settings = AppConfig.Load(_root, environment: Env(null, "or-env"), includeLocalOverrides: false);
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenRouterEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("openai/text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void OpenAiKeyAloneLeavesEndpointAndModelAtDefaults()
    {
        var settings = AppConfig.Load(_root, environment: Env("sk-env", null), includeLocalOverrides: false);
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenAiEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void OpenAiKeyWinsWhenBothAreSet()
    {
        var settings = AppConfig.Load(_root, environment: Env("sk-env", "or-env"), includeLocalOverrides: false);
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenAiEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void ExplicitModelSurvivesAnOpenRouterKey()
    {
        var overrides = new Dictionary<string, string?> { ["OpenAI:Model"] = "custom-model" };
        var settings = AppConfig.Load(_root, overrides, Env(null, "or-env"), includeLocalOverrides: false);
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenRouterEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("custom-model", settings.OpenAI.Model);
    }

    private static Dictionary<string, string?> Endpoint(string endpoint) => new() { ["OpenAI:Endpoint"] = endpoint };

    [Fact]
    public void AnOpenAiKeyIsNeverSentToAnotherEndpoint()
    {
        var toOpenRouter = Assert.Throws<ParityConfigurationException>(() => AppConfig.Load(_root, Endpoint(OpenRouterEndpoint), Env("sk-secret", null), includeLocalOverrides: false));
        Assert.Contains("OPENAI_API_KEY would be sent to 'openrouter.ai'", toOpenRouter.Message);
        Assert.Contains("OpenAI:ApiKey", toOpenRouter.Message);
        Assert.DoesNotContain("sk-secret", toOpenRouter.Message);

        var toProxy = Assert.Throws<ParityConfigurationException>(() => AppConfig.Load(_root, Endpoint("https://proxy.example/v1"), Env("sk-secret", null), includeLocalOverrides: false));
        Assert.Contains("'proxy.example'", toProxy.Message);
    }

    [Fact]
    public void AnOpenRouterKeyIsNeverSentToAnotherEndpoint()
    {
        Assert.Throws<ParityConfigurationException>(() => AppConfig.Load(_root, Endpoint("https://proxy.example/v1"), Env(null, "or-secret"), includeLocalOverrides: false));
        Assert.Throws<ParityConfigurationException>(() => AppConfig.Load(_root, Endpoint("https://api.openai.com/v2"), Env(null, "or-secret"), includeLocalOverrides: false));
    }

    [Fact]
    public void AnExplicitOpenRouterEndpointTakesTheOpenRouterKeyEvenWhenBothAreSet()
    {
        var settings = AppConfig.Load(_root, Endpoint(OpenRouterEndpoint), Env("sk-env", "or-env"), includeLocalOverrides: false);
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenRouterEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("openai/text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void AKeyFromTheConfigurationIsUsedWithAnyHttpsEndpointButNotPlainHttp()
    {
        var overrides = new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "cfg-key", ["OpenAI:Endpoint"] = "https://proxy.example/v1" };
        Assert.Equal("cfg-key", AppConfig.Load(_root, overrides, Env("sk-env", null), includeLocalOverrides: false).OpenAI.ApiKey);

        overrides["OpenAI:Endpoint"] = "http://proxy.example/v1";
        var error = Assert.Throws<ParityConfigurationException>(() => AppConfig.Load(_root, overrides, Env(null, null), includeLocalOverrides: false));
        Assert.Contains("must be an https URL", error.Message);
        Assert.DoesNotContain("cfg-key", error.Message);
    }

    [Fact]
    public void EmptyEnvironmentLeavesTheKeyNull()
    {
        var settings = AppConfig.Load(_root, environment: Env(null, null), includeLocalOverrides: false);
        Assert.Null(settings.OpenAI.ApiKey);
    }

    [Fact]
    public async Task HelpExitsZeroAndUnknownCommandExitsTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApp(output, error, _root, includeLocalOverrides: false);

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["--help"], CancellationToken.None));
        Assert.Contains("generate-data", output.ToString());
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["bogus"], CancellationToken.None));
        Assert.Contains("Unknown command 'bogus'", error.ToString());
    }
}

/// <summary>
/// Sets a process-wide environment variable, so these tests get a collection of their own that xUnit runs
/// without any other collection in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "process-environment";
}

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProcessEnvironmentConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-env-{Guid.NewGuid():N}");

    public ProcessEnvironmentConfigTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Func<string, string?> Env(string? openAi, string? openRouter) =>
        name => name switch { "OPENAI_API_KEY" => openAi, "OPENROUTER_API_KEY" => openRouter, _ => null };

    [Fact]
    public void LocalOverridesAreSkippedWhenIncludeLocalOverridesIsFalse()
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{\"OpenAI\":{\"Model\":\"from-appsettings\"}}");
        const string variable = "PARITY_OpenAI__Model";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "from-env");
            Assert.Equal("from-env", AppConfig.Load(_root, environment: Env(null, null)).OpenAI.Model);
            Assert.Equal("from-appsettings", AppConfig.Load(_root, environment: Env(null, null), includeLocalOverrides: false).OpenAI.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
