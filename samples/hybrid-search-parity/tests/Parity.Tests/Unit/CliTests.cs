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
