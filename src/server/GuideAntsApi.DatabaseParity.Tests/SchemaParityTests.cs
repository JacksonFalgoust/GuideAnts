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

    /// <summary>
    /// Deferrability is left out of the comparison: SQL Server has no deferrable constraints, and the Postgres
    /// foreign keys that are deferred to match SQL Server's check timing are pinned by
    /// <see cref="Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun"/>.
    /// </summary>
    [TestMethod]
    public void Same_foreign_keys()
    {
        static IEnumerable<string> Lines(SchemaSnapshot snapshot) => snapshot.ForeignKeys.Select(f =>
            $"{Name(f.Table)}.{Name(f.Name)}: ({string.Join(", ", f.Columns.Select(Name))}) -> "
            + $"{Name(f.PrincipalTable)} ({string.Join(", ", f.PrincipalColumns.Select(Name))}) ON DELETE {f.DeleteRule}");

        _sqlServer.ForeignKeys.Should().NotBeEmpty();

        ShouldMatch(Lines(_sqlServer), Lines(_postgres), "Foreign keys");
    }

    /// <summary>
    /// SQL Server checks a NO ACTION foreign key after every cascade of the DELETE. Postgres checks it in an AFTER
    /// trigger, which can run before a cascade of the same statement has removed the referencing rows:
    /// <list type="bullet">
    /// <item>The triggers of one deleted row fire in trigger-name order, and the names embed OIDs, so their order
    /// depends on creation order and on the OID counter.</item>
    /// <item>The events that a cascade queues fire after the events already queued, one cascade level at a time.</item>
    /// </list>
    /// So when a root delete reaches the referenced table at cascade depth p and the referencing table at depth d, the
    /// check fires after the referencing rows are gone if d &lt;= p, in the same pass (in name order) if d = p + 1, and
    /// before them if d &gt; p + 1. The foreign keys with d &gt; p for some root are DEFERRABLE INITIALLY DEFERRED on
    /// Postgres (the Postgres baseline migration does this): a deferred NO ACTION check runs at commit, after every
    /// cascade, since Postgres never defers referential actions. Every other foreign key stays NOT DEFERRABLE, so its
    /// check keeps SQL Server's statement-time timing. SQL Server forbids multiple cascade paths, so a table has at most
    /// one depth below a root.
    /// </summary>
    [TestMethod]
    public void Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun()
    {
        const string NotDeferrable = "NOT DEFERRABLE";
        static string Key(string table, string foreignKey) => $"{Name(table)}.{Name(foreignKey)}";

        var cascadeChildren = _sqlServer.ForeignKeys
            .Where(f => f.DeleteRule == "CASCADE")
            .ToLookup(f => f.PrincipalTable, f => f.Table, StringComparer.Ordinal);

        Dictionary<string, int> CascadeDepths(string root)
        {
            var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [root] = 0 };
            var queue = new Queue<string>([root]);
            while (queue.TryDequeue(out var table))
            {
                foreach (var child in cascadeChildren[table])
                {
                    depths.TryAdd(child, depths[table] + 1).Should().BeTrue($"SQL Server allows one cascade path from {root} to {child}");
                    queue.Enqueue(child);
                }
            }
            return depths;
        }

        var depthsByRoot = _sqlServerTables.Select(t => CascadeDepths(t.Name)).ToList();
        var mustDefer = new List<string>();
        var alreadyOrdered = new List<string>();
        foreach (var foreignKey in _sqlServer.ForeignKeys.Where(f => f.DeleteRule == "NO ACTION"))
        {
            var shadows = depthsByRoot
                .Where(d => d.ContainsKey(foreignKey.PrincipalTable) && d.GetValueOrDefault(foreignKey.Table) > 0)
                .Select(d => (Referenced: d[foreignKey.PrincipalTable], Referencing: d[foreignKey.Table]))
                .ToList();

            if (shadows.Any(s => s.Referencing > s.Referenced))
            {
                mustDefer.Add(Key(foreignKey.Table, foreignKey.Name));
            }
            else if (shadows.Count > 0)
            {
                alreadyOrdered.Add(Key(foreignKey.Table, foreignKey.Name));
            }
        }

        // Pinned so that a model change that adds or removes such a shape is reviewed (parity report #19, #23 to #25).
        mustDefer.Should().BeEquivalentTo(
            Key("GuideMembers", "FK_GuideMembers_Assistants_AssistantId"),
            Key("MessageAttachments", "FK_MessageAttachments_NotebookFiles_NotebookFileId"),
            Key("UserRoles", "FK_UserRoles_Users_AssignedByUserId"));
        alreadyOrdered.Should().BeEquivalentTo(
            Key("AgentInvocations", "FK_AgentInvocations_AgentInvocations_ParentInvocationId"),
            Key("ContentFileVersions", "FK_ContentFileVersions_ContentFileVersions_OriginVersionId"),
            Key("NotebookConversationMessages", "FK_NotebookConversationMessages_ConversationTurns_NotebookConversationId_TurnIndex"));

        _sqlServer.ForeignKeys.Should().OnlyContain(f => f.Deferrability == NotDeferrable);
        ShouldMatch(
            mustDefer.Select(k => $"{k}: DEFERRABLE INITIALLY DEFERRED"),
            _postgres.ForeignKeys.Where(f => f.Deferrability != NotDeferrable).Select(f => $"{Key(f.Table, f.Name)}: {f.Deferrability}"),
            "Deferred foreign keys");
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
