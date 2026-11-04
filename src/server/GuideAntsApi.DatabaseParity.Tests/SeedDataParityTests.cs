using System.Data.Common;
using System.Text;
using FluentAssertions;
using GuideAntsApi.DatabaseParity.Tests.Infrastructure;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.DatabaseParity.Tests;

/// <summary>
/// A freshly migrated Postgres database holds the same rows as a freshly migrated SQL Server database: the HasData
/// seed plus everything the SQL Server migrations insert or backfill with raw T-SQL. Both databases are migrated
/// here, on the shared servers, so rows other tests write to the shared databases can't affect the result.
/// Values are compared through <see cref="TableRows"/>'s provider-neutral strings.
/// </summary>
[TestClass]
public sealed class SeedDataParityTests
{
    private const int MaxShownValueLength = 160;

    /// <summary>
    /// Migration 20260619195619 (SeedUsageReportCategoriesAndOperations) inserts these rows with <c>NEWID()</c>, so
    /// their ids differ between any two fresh SQL Server databases too. Their rows are matched on this unique natural
    /// key instead, and an id that points at one of these rows (its own primary key, or a foreign key to it) is
    /// compared as that row's natural key.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> GeneratedIdNaturalKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UsageReportCategories"] = "Key",
        ["UsageReportCategoryOperations"] = "Operation",
    };

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Every_table_has_the_same_rows_after_a_fresh_migrate()
    {
        var database = $"parity_seed_{Guid.NewGuid():N}";
        await using var sqlServer = ParityDatabases.CreateContext(
            DatabaseProvider.SqlServer, ParityAssembly.Databases.SqlServerConnectionStringFor(database));
        await using var postgres = ParityDatabases.CreateContext(
            DatabaseProvider.Postgres, ParityAssembly.Databases.PostgresConnectionStringFor(database));
        try
        {
            await Task.WhenAll(sqlServer.Database.MigrateAsync(), postgres.Database.MigrateAsync());

            var tables = await ReadTablesAsync(sqlServer.Database.GetDbConnection(), postgres.Database.GetDbConnection());

            tables.Sum(t => t.SqlServerRows.Count).Should().BeGreaterThan(0, "the SQL Server migrations seed rows, so an empty comparison would prove nothing");
            foreach (var table in tables.Where(t => t.SqlServerRows.Count > 0 || t.PostgresRows.Count > 0))
            {
                TestContext.WriteLine($"{table.Name}: SQL Server {table.SqlServerRows.Count} rows, Postgres {table.PostgresRows.Count} rows");
            }

            var differences = tables.Select(Describe).OfType<string>().ToList();
            if (differences.Count > 0)
            {
                Assert.Fail($"Rows differ in {differences.Count} tables.{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
            }
        }
        finally
        {
            await Task.WhenAll(sqlServer.Database.EnsureDeletedAsync(), postgres.Database.EnsureDeletedAsync());
        }
    }

    /// <param name="Columns">SQL Server column names, rowversion left out.</param>
    /// <param name="MatchOn">The columns that identify a row: the primary key, or the natural key for generated ids.</param>
    private sealed record TableData(
        string Name,
        List<string> Columns,
        IReadOnlyList<string> MatchOn,
        IReadOnlyList<IReadOnlyList<string>> SqlServerRows,
        IReadOnlyList<IReadOnlyList<string>> PostgresRows);

    private static async Task<List<TableData>> ReadTablesAsync(DbConnection sqlServer, DbConnection postgres)
    {
        var sqlServerSchema = await SchemaSnapshot.ReadAsync(sqlServer, DatabaseProvider.SqlServer);
        var postgresSchema = await SchemaSnapshot.ReadAsync(postgres, DatabaseProvider.Postgres);
        var sqlServerKeys = await TableRows.PrimaryKeysAsync(sqlServer, DatabaseProvider.SqlServer);
        var postgresKeys = await TableRows.PrimaryKeysAsync(postgres, DatabaseProvider.Postgres);

        // Same_tables covers the table lists; only the never-read SQL Server backup table may be one-sided.
        sqlServerSchema.Tables.Keys.Select(Name).Except(postgresSchema.Tables.Keys)
            .Should().BeEquivalentTo(new[] { SchemaParityTests.SqlServerBackupTable });

        // It copies ApplicationSettings rows that exist before migration 20260414183022, and a fresh database has none
        // then, so leaving the table out of Postgres leaves out no rows.
        await using (var count = sqlServer.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM [{SchemaParityTests.SqlServerBackupTable}]";
            Convert.ToInt32(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be(0, "a fresh SQL Server database has nothing to back up");
        }

        var tables = new List<TableData>();
        foreach (var table in sqlServerSchema.Tables.Values
                     .Where(t => postgresSchema.Tables.ContainsKey(Name(t.Name)))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var postgresTable = postgresSchema.Tables[Name(table.Name)];
            var key = sqlServerKeys[table.Name];
            postgresKeys[postgresTable.Name].Should().Equal(key.Select(Name), $"{table.Name} has the same primary key on both providers");

            // rowversion has no Postgres user column (xmin is a system column); other one-sided columns fail Same_columns.
            var postgresColumns = postgresTable.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var columns = table.Columns
                .Where(c => c.TypeCategory != SchemaSnapshot.RowVersionCategory && postgresColumns.Contains(Name(c.Name)))
                .Select(c => c.Name)
                .ToList();

            var sqlServerRows = await TableRows.ReadAsync(sqlServer, DatabaseProvider.SqlServer, table.Name, columns, key);
            var postgresRows = await TableRows.ReadAsync(
                postgres, DatabaseProvider.Postgres, postgresTable.Name, columns.Select(Name).ToList(), key.Select(Name).ToList());

            var matchOn = GeneratedIdNaturalKeys.TryGetValue(table.Name, out var naturalKey) ? new[] { naturalKey } : key;
            tables.Add(new TableData(table.Name, columns, matchOn, sqlServerRows, postgresRows));
        }

        // Replace each generated id, wherever it appears as a primary or foreign key, with the natural key of its row.
        var references = sqlServerSchema.ForeignKeys
            .Where(f => GeneratedIdNaturalKeys.ContainsKey(f.PrincipalTable) && f.Columns.Count == 1)
            .Select(f => (f.Table, Column: f.Columns[0]))
            .Concat(GeneratedIdNaturalKeys.Keys.Select(t => (Table: t, Column: sqlServerKeys[t].Single())))
            .ToLookup(r => r.Table, r => r.Column, StringComparer.Ordinal);
        var sqlServerIds = GeneratedIdNames(tables, t => t.SqlServerRows);
        var postgresIds = GeneratedIdNames(tables, t => t.PostgresRows);

        return tables
            .Select(t => t with
            {
                SqlServerRows = ReplaceIds(t, references[t.Name], t.SqlServerRows, sqlServerIds),
                PostgresRows = ReplaceIds(t, references[t.Name], t.PostgresRows, postgresIds),
            })
            .ToList();
    }

    /// <summary>Generated id → <c>Table[NaturalKey='value']</c>, for every row of the generated-id tables.</summary>
    private static Dictionary<string, string> GeneratedIdNames(
        IEnumerable<TableData> tables, Func<TableData, IReadOnlyList<IReadOnlyList<string>>> rowsOf)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables.Where(t => GeneratedIdNaturalKeys.ContainsKey(t.Name)))
        {
            var naturalKey = table.MatchOn.Single();
            var idIndex = table.Columns.IndexOf("Id");
            var keyIndex = table.Columns.IndexOf(naturalKey);
            idIndex.Should().BeGreaterThanOrEqualTo(0, $"{table.Name} has an Id column");
            foreach (var row in rowsOf(table))
            {
                names[row[idIndex]] = $"{table.Name}[{naturalKey}={row[keyIndex]}]";
            }
        }
        return names;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReplaceIds(
        TableData table,
        IEnumerable<string> idColumns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> names)
    {
        var indexes = idColumns.Select(c => table.Columns.IndexOf(c)).Where(i => i >= 0).ToArray();
        if (indexes.Length == 0)
        {
            return rows;
        }

        return rows
            .Select(row =>
            {
                var copy = row.ToArray();
                foreach (var i in indexes)
                {
                    copy[i] = names.TryGetValue(copy[i], out var name) ? name : copy[i];
                }
                return (IReadOnlyList<string>)copy;
            })
            .ToList();
    }

    /// <summary>Every row only one provider has, every value that differs and every duplicate match key; null when none.</summary>
    private static string? Describe(TableData table)
    {
        var columns = table.Columns;
        var keyIndexes = table.MatchOn.Select(k => columns.IndexOf(k)).ToArray();
        string KeyOf(IReadOnlyList<string> row) => string.Join(", ", keyIndexes.Select(i => $"{columns[i]}={row[i]}"));
        string Show(IReadOnlyList<string> row) => string.Join(", ", columns.Select((c, i) => $"{c}={Truncate(row[i])}"));

        var lines = new List<string>();
        foreach (var (provider, rows) in new[] { ("SQL Server", table.SqlServerRows), ("Postgres", table.PostgresRows) })
        {
            lines.AddRange(rows.GroupBy(KeyOf, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => $"  Duplicate on {provider}: [{g.Key}] appears {g.Count()} times"));
        }

        var postgresByKey = table.PostgresRows.GroupBy(KeyOf, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sqlServerKeys = table.SqlServerRows.Select(KeyOf).ToHashSet(StringComparer.Ordinal);

        foreach (var row in table.SqlServerRows)
        {
            if (!postgresByKey.TryGetValue(KeyOf(row), out var postgresRow))
            {
                lines.Add($"  SQL Server only: {Show(row)}");
                continue;
            }

            for (var i = 0; i < columns.Count; i++)
            {
                if (!string.Equals(row[i], postgresRow[i], StringComparison.Ordinal))
                {
                    lines.Add(
                        $"  Different [{KeyOf(row)}] {columns[i]}: SQL Server {Truncate(row[i])} | Postgres {Truncate(postgresRow[i])}"
                        + $" (first difference at char {FirstDifference(row[i], postgresRow[i])})");
                }
            }
        }

        lines.AddRange(table.PostgresRows.Where(r => !sqlServerKeys.Contains(KeyOf(r))).Select(r => $"  Postgres only: {Show(r)}"));

        if (lines.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder($"{table.Name} (SQL Server {table.SqlServerRows.Count} rows, Postgres {table.PostgresRows.Count} rows)");
        foreach (var line in lines)
        {
            text.Append(Environment.NewLine).Append(line);
        }
        return text.ToString();
    }

    private static int FirstDifference(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }
        return length;
    }

    private static string Truncate(string value)
        => value.Length <= MaxShownValueLength ? value : $"{value[..MaxShownValueLength]}... ({value.Length} chars)";

    /// <summary>The Postgres name for an identifier; names that already fit are unchanged.</summary>
    private static string Name(string identifier) => PostgresModelAdapter.ShortenIdentifier(identifier);
}
