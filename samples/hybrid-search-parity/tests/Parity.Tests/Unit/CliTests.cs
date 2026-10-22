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
        var settings = AppConfig.Load(RepoPaths.FindSampleRoot(), environment: _ => null);

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
    public void OpenAiKeyFallsBackToTheEnvironment()
    {
        var settings = AppConfig.Load(_root, new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "" }, name => name == "OPENAI_API_KEY" ? "sk-env" : null);
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
    }

    private const string OpenAiEndpoint = "https://api.openai.com/v1";
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";

    private static Func<string, string?> Env(string? openAi, string? openRouter) =>
        name => name switch { "OPENAI_API_KEY" => openAi, "OPENROUTER_API_KEY" => openRouter, _ => null };

    [Fact]
    public void OpenRouterKeyAloneSwitchesKeyEndpointAndModel()
    {
        var settings = AppConfig.Load(_root, environment: Env(null, "or-env"));
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenRouterEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("openai/text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void OpenAiKeyAloneLeavesEndpointAndModelAtDefaults()
    {
        var settings = AppConfig.Load(_root, environment: Env("sk-env", null));
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
        Assert.Equal(OpenAiEndpoint, settings.OpenAI.Endpoint);
        Assert.Equal("text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void OpenAiKeyWinsWhenBothAreSet()
    {
        var settings = AppConfig.Load(_root, environment: Env("sk-env", "or-env"));
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
        var settings = AppConfig.Load(_root, overrides, Env(null, "or-env"));
        Assert.Equal("or-env", settings.OpenAI.ApiKey);
        Assert.Equal("https://proxy.example/v1", settings.OpenAI.Endpoint);
        Assert.Equal("custom-model", settings.OpenAI.Model);
    }

    [Fact]
    public void EmptyEnvironmentLeavesTheKeyNull()
    {
        var settings = AppConfig.Load(_root, environment: Env(null, null));
        Assert.Null(settings.OpenAI.ApiKey);
    }

    [Fact]
    public async Task HelpExitsZeroAndUnknownCommandExitsTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApp(output, error, _root);

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["--help"], CancellationToken.None));
        Assert.Contains("generate-data", output.ToString());
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["bogus"], CancellationToken.None));
        Assert.Contains("Unknown command 'bogus'", error.ToString());
    }
}
