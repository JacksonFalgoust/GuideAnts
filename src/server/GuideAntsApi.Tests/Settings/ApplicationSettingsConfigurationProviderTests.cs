using FluentAssertions;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.Settings;
using System.Text.Json.Nodes;

namespace GuideAntsApi.Tests.Settings;

[TestClass]
public sealed class ApplicationSettingsConfigurationProviderTests
{
    [TestMethod]
    public void Load_WithEmptyConnectionString_DoesNotThrow()
    {
        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: string.Empty,
            provider: DatabaseProvider.SqlServer,
            registry: new SettingsSectionRegistry(),
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        Action act = () => provider.Load();

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Load_WithInvalidConnectionString_ThrowsLoudStartupException()
    {
        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: "not-a-connection-string",
            provider: DatabaseProvider.SqlServer,
            registry: new SettingsSectionRegistry(),
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        Action act = () => provider.Load();

        act
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Failed to load DB-backed application settings*")
            .WithMessage("*Startup was aborted*");
    }

    [TestMethod]
    public void Load_OnPostgres_WithInvalidConnectionString_ThrowsLoudStartupException()
    {
        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: "not-a-connection-string",
            provider: DatabaseProvider.Postgres,
            registry: new SettingsSectionRegistry(),
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        Action act = () => provider.Load();

        act
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Failed to load DB-backed application settings from '<unparseable-connection-string>'*")
            .WithMessage("*Startup was aborted*");
    }

    [TestMethod]
    public void Load_OnPostgres_WhenServerIsUnreachable_NamesHostAndDatabase()
    {
        // Nothing listens on port 1, so the connection is refused at once and the error names the target.
        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: "Host=127.0.0.1;Port=1;Database=guideants_settings;Username=guideants;Password=unused;Timeout=2",
            provider: DatabaseProvider.Postgres,
            registry: new SettingsSectionRegistry(),
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        Action act = () => provider.Load();

        act
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Failed to load DB-backed application settings from '127.0.0.1/guideants_settings'*")
            .WithInnerException<Npgsql.NpgsqlException>();
    }

    [TestMethod]
    public void FlattenSection_EmitsEmptyCanonicalKeys_ForNullChatDefaultsFields()
    {
        var registry = new SettingsSectionRegistry();
        registry.TryGet("ChatDefaults", out var definition).Should().BeTrue();

        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: string.Empty,
            provider: DatabaseProvider.SqlServer,
            registry: registry,
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        var payload = new JsonObject
        {
            ["DefaultModelId"] = "gpt-5.5",
            ["OverrideAllChatModels"] = true,
            ["Temperature"] = null,
            ["TopP"] = null,
            ["ReasoningEffort"] = "low",
            ["SamplingParametersJson"] = null
        };

        provider.GetType()
            .GetMethod("FlattenSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(provider, ["ChatDefaults", payload, definition]);

        provider.TryGet("ChatDefaults:Temperature", out var temperature).Should().BeTrue();
        temperature.Should().BeEmpty();
        provider.TryGet("ChatDefaults:TopP", out var topP).Should().BeTrue();
        topP.Should().BeEmpty();
        provider.TryGet("ChatDefaults:ReasoningEffort", out var reasoning).Should().BeTrue();
        reasoning.Should().Be("low");
    }

    [TestMethod]
    public void FlattenSection_DoesNotShadowMissingLlamaCppBaseUrl()
    {
        var registry = new SettingsSectionRegistry();
        registry.TryGet("LlamaCpp", out var definition).Should().BeTrue();

        var provider = new ApplicationSettingsConfigurationProvider(
            connectionString: string.Empty,
            provider: DatabaseProvider.SqlServer,
            registry: registry,
            contentRootPath: AppContext.BaseDirectory,
            settingsSecrets: CreateValidSecretsOptions());

        var payload = new JsonObject();

        provider.GetType()
            .GetMethod("FlattenSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(provider, ["LlamaCpp", payload, definition]);

        provider.TryGet("LlamaCpp:BaseUrl", out _).Should().BeFalse();
    }

    private static SettingsSecretsOptions CreateValidSecretsOptions() => new()
    {
        ActiveKeyId = "tests",
        Keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tests"] = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
        }
    };
}
