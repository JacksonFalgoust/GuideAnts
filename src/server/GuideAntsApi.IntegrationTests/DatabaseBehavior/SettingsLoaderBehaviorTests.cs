using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Settings;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// The DB-backed settings loader (<see cref="ApplicationSettingsConfigurationProvider"/>) runs at host startup,
/// before DI exists, on a raw ADO.NET connection. These tests run it directly against the suite's database on
/// either provider: it reads the rows of <c>ApplicationSettings</c>, and it fails loudly when the table is missing.
/// </summary>
[TestClass]
public sealed class SettingsLoaderBehaviorTests
{
    private static readonly SettingsSecretsOptions SettingsSecrets = new()
    {
        ActiveKeyId = "tests",
        Keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tests"] = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
        }
    };

    [TestMethod]
    public async Task Settings_loader_reads_application_settings_rows()
    {
        var containers = TestContainerManager.Instance;
        var connectionString = await containers.GetConnectionStringAsync();
        var sectionName = "LoaderProbe" + Guid.NewGuid().ToString("N")[..8];

        await using var services = CreateServices(containers.Provider, connectionString);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ApplicationSettings.Add(new ApplicationSetting
            {
                SectionName = sectionName,
                JsonValue = """{"Mode":"probe","Nested":{"Inner":"value"}}"""
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var loader = CreateLoader(connectionString, containers.Provider);

            loader.Load();

            loader.TryGet($"{sectionName}:Mode", out var mode).Should().BeTrue();
            mode.Should().Be("probe");
            loader.TryGet($"{sectionName}:Nested:Inner", out var inner).Should().BeTrue();
            inner.Should().Be("value");
        }
        finally
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.ApplicationSettings.Where(s => s.SectionName == sectionName).ExecuteDeleteAsync();
        }
    }

    [TestMethod]
    public async Task Settings_loader_fails_loudly_when_the_application_settings_table_is_missing()
    {
        var containers = TestContainerManager.Instance;
        var suiteConnectionString = await containers.GetConnectionStringAsync();
        var emptyDatabase = "ga_no_settings_" + Guid.NewGuid().ToString("N")[..8];
        var emptyConnectionString = await CreateEmptyDatabaseAsync(containers.Provider, suiteConnectionString, emptyDatabase);

        try
        {
            var loader = CreateLoader(emptyConnectionString, containers.Provider);

            var act = () => loader.Load();

            var expectedMessage = containers.Provider == DatabaseProvider.Postgres
                ? "Required table 'ApplicationSettings' was not found while loading DB-backed application settings."
                : "Required table 'dbo.ApplicationSettings' was not found while loading DB-backed application settings.";
            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage($"Failed to load DB-backed application settings from '*/{emptyDatabase}'*")
                .WithInnerException<InvalidOperationException>()
                .WithMessage(expectedMessage);
        }
        finally
        {
            await DropDatabaseAsync(containers.Provider, suiteConnectionString, emptyDatabase);
        }
    }

    private static ApplicationSettingsConfigurationProvider CreateLoader(string connectionString, DatabaseProvider provider) =>
        new(connectionString, provider, new SettingsSectionRegistry(), AppContext.BaseDirectory, SettingsSecrets);

    private static ServiceProvider CreateServices(DatabaseProvider provider, string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            ApplicationDbContextOptions.Configure(
                options,
                provider,
                connectionString,
                new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true)));
        return services.BuildServiceProvider();
    }

    /// <summary>Creates an empty database next to the suite's one and returns an unpooled connection string to it.</summary>
    private static async Task<string> CreateEmptyDatabaseAsync(DatabaseProvider provider, string suiteConnectionString, string name)
    {
        if (provider == DatabaseProvider.Postgres)
        {
            await using var connection = new NpgsqlConnection(suiteConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
            return new NpgsqlConnectionStringBuilder(suiteConnectionString) { Database = name, Pooling = false }.ConnectionString;
        }

        await using (var connection = new SqlConnection(suiteConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(suiteConnectionString) { InitialCatalog = name, Pooling = false }.ConnectionString;
    }

    private static async Task DropDatabaseAsync(DatabaseProvider provider, string suiteConnectionString, string name)
    {
        if (provider == DatabaseProvider.Postgres)
        {
            await using var connection = new NpgsqlConnection(suiteConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
            return;
        }

        await using (var connection = new SqlConnection(suiteConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }
    }
}
