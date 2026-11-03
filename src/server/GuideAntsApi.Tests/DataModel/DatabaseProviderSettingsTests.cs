using FluentAssertions;
using GuideAntsApi.DataModel.Database;

namespace GuideAntsApi.Tests.DataModel;

[TestClass]
public sealed class DatabaseProviderSettingsTests
{
    [TestMethod]
    public void Parse_returns_SqlServer_for_null_empty_and_whitespace()
    {
        DatabaseProviderSettings.Parse(null).Should().Be(DatabaseProvider.SqlServer);
        DatabaseProviderSettings.Parse("").Should().Be(DatabaseProvider.SqlServer);
        DatabaseProviderSettings.Parse("  ").Should().Be(DatabaseProvider.SqlServer);
    }

    [TestMethod]
    public void Parse_is_case_insensitive()
    {
        DatabaseProviderSettings.Parse("postgres").Should().Be(DatabaseProvider.Postgres);
        DatabaseProviderSettings.Parse("POSTGRES").Should().Be(DatabaseProvider.Postgres);
        DatabaseProviderSettings.Parse("sqlserver").Should().Be(DatabaseProvider.SqlServer);
    }

    [TestMethod]
    public void Parse_rejects_unknown_values()
    {
        var act = () => DatabaseProviderSettings.Parse("mysql");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Unknown Database:Provider 'mysql'. Use 'SqlServer' or 'Postgres'.");
    }

    [TestMethod]
    public void FromEnvironment_falls_through_empty_colon_key_to_double_underscore_key()
    {
        var colon = Environment.GetEnvironmentVariable("Database:Provider");
        var underscore = Environment.GetEnvironmentVariable("Database__Provider");
        try
        {
            Environment.SetEnvironmentVariable("Database:Provider", "");
            Environment.SetEnvironmentVariable("Database__Provider", "Postgres");
            DatabaseProviderSettings.FromEnvironment().Should().Be(DatabaseProvider.Postgres);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database:Provider", colon);
            Environment.SetEnvironmentVariable("Database__Provider", underscore);
        }
    }
}
