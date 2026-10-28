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

        Assert.Equal(new[] { "postgres", "postgres-english", "sqlserver" }, settings.Profiles.Keys.Order());
        Assert.Equal("SqlServer", settings.Profiles["sqlserver"].Provider);
        Assert.Equal("parity_english_nostem", settings.Profiles["postgres"].TextSearchConfig);
        Assert.Equal("english", settings.Profiles["postgres-english"].TextSearchConfig);
        Assert.Equal(new[] { "postgres-english" }, settings.Parity.Informational);
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
    public async Task GitStateIgnoresResultsMdButNotOtherSampleChanges()
    {
        var repo = Path.Combine(_root, "repo");
        var sample = Path.Combine(repo, "sample");
        Directory.CreateDirectory(sample);
        File.WriteAllText(Path.Combine(sample, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(sample, "RESULTS.md"), "old");
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
    public void ExplicitEndpointAndModelSurviveAnOpenRouterKey()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["OpenAI:Endpoint"] = "https://proxy.example/v1",
            ["OpenAI:Model"] = "custom-model",
        };
        var settings = AppConfig.Load(_root, overrides, Env(null, "or-env"), includeLocalOverrides: false);
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal("https://proxy.example/v1", settings.OpenAI.Endpoint);
        Assert.Equal("custom-model", settings.OpenAI.Model);
    }

    [Fact]
    public void EmptyEnvironmentLeavesTheKeyNull()
    {
        var settings = AppConfig.Load(_root, environment: Env(null, null), includeLocalOverrides: false);
        Assert.Null(settings.OpenAI.ApiKey);
    }

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
