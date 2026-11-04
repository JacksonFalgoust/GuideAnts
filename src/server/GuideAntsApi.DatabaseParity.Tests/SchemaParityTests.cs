using FluentAssertions;
using GuideAntsApi.DatabaseParity.Tests.Infrastructure;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.DatabaseParity.Tests;

/// <summary>
/// The schema that the Postgres migrations build matches the one the SQL Server migrations build,
/// compared through <see cref="SchemaSnapshot"/>'s provider-neutral categories. SQL Server names are compared
/// after <see cref="PostgresModelAdapter.ShortenIdentifier"/>, the adapter's mapping for names over 63 characters.
/// </summary>
[TestClass]
public sealed class SchemaParityTests
{
    /// <summary>
    /// Raw-SQL backup of the rows that migration 20260414183022 (RemoveApplicationSettingsConfigModeAndProviderRouting)
    /// rewrote. It is not in the model and no code reads it; a new Postgres database has nothing to back up.
    /// </summary>
    internal const string SqlServerBackupTable = "ApplicationSettingsBackup_20260414_ProviderRouting";

    /// <summary>
    /// Migration 20260625193000 (AddConversationTurnTraceCapture) has no designer file, so SQL Server's migrations SQL
    /// generator had no model to read, treated the column as nullable and added <c>WHERE [ConversationTurnId] IS NOT NULL</c>.
    /// The column is NOT NULL, so the filter changes nothing, and the model (and so Postgres) has none.
    /// </summary>
    private static readonly (string Table, string Index, string Column) NoOpSqlServerFilter =
        ("ConversationTurnTraces", "IX_ConversationTurnTraces_ConversationTurnId", "ConversationTurnId");

    private static SchemaSnapshot _sqlServer = null!;
    private static SchemaSnapshot _postgres = null!;
    private static IReadOnlyList<SchemaTable> _sqlServerTables = null!;

    [ClassInitialize]
    public static async Task ReadSnapshotsAsync(TestContext _)
    {
        _sqlServer = await ReadSnapshotAsync(DatabaseProvider.SqlServer);
        _postgres = await ReadSnapshotAsync(DatabaseProvider.Postgres);
        _sqlServerTables = _sqlServer.Tables.Values.Where(t => t.Name != SqlServerBackupTable).ToList();
    }

    [TestMethod]
    public async Task Postgres_baseline_migrates_an_empty_database()
    {
        var database = $"parity_fresh_{Guid.NewGuid():N}";
        await using var context = ParityDatabases.CreateContext(
            DatabaseProvider.Postgres, ParityAssembly.Databases.PostgresConnectionStringFor(database));
        try
        {
            await context.Database.MigrateAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.Should().ContainSingle().Which.Should().EndWith("_PostgresBaselineV1");
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [TestMethod]
    public void Same_tables()
    {
        _sqlServerTables.Should().NotBeEmpty();
        _sqlServer.Tables.Should().ContainKey(SqlServerBackupTable);

        ShouldMatch(_sqlServerTables.Select(t => Name(t.Name)), _postgres.Tables.Keys, "Tables");
    }

    [TestMethod]
    public void Same_columns()
    {
        // SQL Server's rowversion columns have no Postgres user column: the xmin system column takes their place.
        var rowVersions = _sqlServerTables
            .SelectMany(t => t.Columns.Where(c => c.TypeCategory == SchemaSnapshot.RowVersionCategory).Select(c => (Table: t.Name, c.Name)))
            .ToList();
        rowVersions.Select(r => r.Table).Should().BeEquivalentTo(
            "ApplicationSettings", "JobQueue", "LocalModelOperations", "LocalModelInstallations", "ProjectScheduledJobs");
        foreach (var (table, column) in rowVersions)
        {
            _postgres.Tables.Should().ContainKey(Name(table));
            _postgres.Tables[Name(table)].Columns.Select(c => c.Name).Should().NotContain(new[] { column, "xmin" });
        }

        ShouldMatch(
            ColumnLines(_sqlServerTables.Select(t => (t.Name, t.Columns.Where(c => c.TypeCategory != SchemaSnapshot.RowVersionCategory))), nullability: true),
            ColumnLines(_postgres.Tables.Values.Select(t => (t.Name, t.Columns.AsEnumerable())), nullability: true),
            "Columns");
    }

    [TestMethod]
    public void Same_indexes()
    {
        static IEnumerable<string> Lines(IEnumerable<SchemaTable> tables, Func<SchemaTable, SchemaIndex, bool> hasFilter)
            => tables.SelectMany(t => t.Indexes.Select(i =>
                $"{Name(t.Name)}.{Name(i.Name)}: ({string.Join(", ", i.Columns.Select(Name))})"
                + (i.IncludedColumns.Count > 0 ? $" INCLUDE ({string.Join(", ", i.IncludedColumns.Select(Name))})" : "")
                + (i.IsUnique ? " unique" : "")
                + (hasFilter(t, i) ? " filtered" : "")));

        var sqlServerIndexes = _sqlServerTables.SelectMany(t => t.Indexes).ToList();
        sqlServerIndexes.Should().Contain(i => i.HasFilter);
        sqlServerIndexes.Count(i => i.IncludedColumns.Count > 0).Should().Be(8, "the model declares 8 covering indexes");

        var noOpTable = _sqlServer.Tables[NoOpSqlServerFilter.Table];
        noOpTable.Indexes.Single(i => i.Name == NoOpSqlServerFilter.Index).HasFilter.Should().BeTrue();
        noOpTable.Columns.Single(c => c.Name == NoOpSqlServerFilter.Column).IsNullable.Should().BeFalse();

        ShouldMatch(
            Lines(_sqlServerTables, (t, i) => i.HasFilter && (t.Name, i.Name) != (NoOpSqlServerFilter.Table, NoOpSqlServerFilter.Index)),
            Lines(_postgres.Tables.Values, (_, i) => i.HasFilter),
            "Indexes");
    }

    [TestMethod]
    public void Same_foreign_keys()
    {
        static IEnumerable<string> Lines(SchemaSnapshot snapshot) => snapshot.ForeignKeys.Select(f =>
            $"{Name(f.Table)}.{Name(f.Name)}: ({string.Join(", ", f.Columns.Select(Name))}) -> "
            + $"{Name(f.PrincipalTable)} ({string.Join(", ", f.PrincipalColumns.Select(Name))}) ON DELETE {f.DeleteRule}");

        _sqlServer.ForeignKeys.Should().NotBeEmpty();

        ShouldMatch(Lines(_sqlServer), Lines(_postgres), "Foreign keys");
    }

    [TestMethod]
    public void Same_views()
    {
        // The model maps ConversationCurrentState to a view that the migrations create with raw SQL.
        _sqlServer.Views.Should().ContainKey("ConversationCurrentState");

        // View columns are compared without nullability: Postgres reports every view column as nullable.
        ShouldMatch(
            ColumnLines(_sqlServer.Views.Values.Select(v => (v.Name, v.Columns.AsEnumerable())), nullability: false),
            ColumnLines(_postgres.Views.Values.Select(v => (v.Name, v.Columns.AsEnumerable())), nullability: false),
            "Views");
    }

    private static async Task<SchemaSnapshot> ReadSnapshotAsync(DatabaseProvider provider)
    {
        await using var context = ParityAssembly.Databases.CreateContext(provider);
        var connection = context.Database.GetDbConnection();
        try
        {
            return await SchemaSnapshot.ReadAsync(connection, provider);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static IEnumerable<string> ColumnLines(
        IEnumerable<(string Relation, IEnumerable<SchemaColumn> Columns)> relations, bool nullability)
        => relations.SelectMany(r => r.Columns.Select(c =>
            $"{Name(r.Relation)}.{Name(c.Name)}: {c.TypeCategory}" + (nullability ? (c.IsNullable ? " null" : " not null") : "")));

    /// <summary>The Postgres name for an identifier; names that already fit (all Postgres ones) are unchanged.</summary>
    private static string Name(string identifier) => PostgresModelAdapter.ShortenIdentifier(identifier);

    /// <summary>Fails with every line that only one provider has, so a single run lists all differences.</summary>
    private static void ShouldMatch(IEnumerable<string> sqlServer, IEnumerable<string> postgres, string what)
    {
        var sqlServerLines = sqlServer.ToHashSet(StringComparer.Ordinal);
        var postgresLines = postgres.ToHashSet(StringComparer.Ordinal);
        var sqlServerOnly = sqlServerLines.Except(postgresLines).Order(StringComparer.Ordinal).ToList();
        var postgresOnly = postgresLines.Except(sqlServerLines).Order(StringComparer.Ordinal).ToList();

        if (sqlServerOnly.Count == 0 && postgresOnly.Count == 0)
        {
            return;
        }

        Assert.Fail(
            $"{what} differ.{Environment.NewLine}"
            + $"SQL Server only ({sqlServerOnly.Count}):{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", sqlServerOnly)}{Environment.NewLine}"
            + $"Postgres only ({postgresOnly.Count}):{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", postgresOnly)}");
    }
}
