using System.Text.RegularExpressions;
using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// Smoke test of the DDL that the adapted model produces on Postgres.
/// <c>GenerateCreateScript</c> needs no database connection.
/// </summary>
[TestClass]
public sealed class PostgresCreateScriptTests
{
    private static readonly Lazy<string> Script = new(() =>
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder, DatabaseProvider.Postgres, "Host=localhost;Database=model_only", new DbProviderTuning());
        using var context = new ApplicationDbContext(builder.Options);
        return context.Database.GenerateCreateScript();
    });

    [TestMethod]
    public void Create_script_declares_and_uses_the_case_insensitive_collation()
    {
        Script.Value.Should().Contain("CREATE COLLATION");
        Script.Value.Should().Contain($"COLLATE {PostgresModelAdapter.CaseInsensitiveCollation}");
    }

    [TestMethod]
    public void Create_script_emits_covering_indexes()
    {
        Script.Value.Should().Contain("INCLUDE (");
    }

    [TestMethod]
    public void Create_tables_have_no_xmin_column()
    {
        var createTables = Regex.Matches(Script.Value, @"CREATE TABLE [\s\S]*?\n\);")
            .Select(m => m.Value)
            .ToList();

        createTables.Should().NotBeEmpty();
        createTables.Should().HaveCount(Regex.Matches(Script.Value, "CREATE TABLE ").Count);
        createTables.Should().Contain(sql => sql.Contains("\"ApplicationSettings\""));
        createTables.Should().NotContain(sql => sql.Contains("xmin", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void No_identifier_is_cut_with_a_tilde()
    {
        var identifiers = Regex.Matches(Script.Value, "\"((?:[^\"]|\"\")*)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        identifiers.Should().NotBeEmpty();
        identifiers.Should().NotContain(identifier => identifier.Contains('~'));
    }
}
