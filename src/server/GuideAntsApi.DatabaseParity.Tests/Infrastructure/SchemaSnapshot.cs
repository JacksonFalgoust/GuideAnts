using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using GuideAntsApi.DataModel.Database;

namespace GuideAntsApi.DatabaseParity.Tests.Infrastructure;

/// <param name="TypeCategory">A provider-neutral type, see <see cref="SchemaSnapshot"/>.</param>
public sealed record SchemaColumn(string Name, bool IsNullable, string TypeCategory);

/// <param name="Columns">Key columns in index order.</param>
/// <param name="IncludedColumns">Non-key (INCLUDE) columns, in declaration order.</param>
public sealed record SchemaIndex(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> IncludedColumns,
    bool IsUnique,
    bool HasFilter);

/// <param name="DeleteRule">The SQL-standard action: NO ACTION, RESTRICT, CASCADE, SET NULL or SET DEFAULT.</param>
public sealed record SchemaForeignKey(
    string Name,
    string Table,
    IReadOnlyList<string> Columns,
    string PrincipalTable,
    IReadOnlyList<string> PrincipalColumns,
    string DeleteRule);

public sealed record SchemaTable(string Name, IReadOnlyList<SchemaColumn> Columns, IReadOnlyList<SchemaIndex> Indexes);

public sealed record SchemaView(string Name, IReadOnlyList<SchemaColumn> Columns);

/// <summary>
/// The tables, views, columns, indexes and foreign keys of the default schema (<c>dbo</c> on SQL Server,
/// <c>public</c> on Postgres), read from the system catalogs. <c>__EFMigrationsHistory</c> is left out.
/// Column types are reduced to provider-neutral categories so the two databases can be compared directly:
/// <list type="bullet">
/// <item><c>nvarchar</c>/<c>varchar</c>/<c>text</c> → <c>text(n)</c>, or <c>text(max)</c> when unbounded</item>
/// <item><c>smallint</c>/<c>tinyint</c> → <c>int16</c>, <c>int</c>/<c>integer</c> → <c>int32</c>, <c>bigint</c> → <c>int64</c></item>
/// <item><c>bit</c>/<c>boolean</c> → <c>boolean</c>; <c>datetime2</c>/<c>timestamp with time zone</c> → <c>timestamp</c></item>
/// <item><c>uniqueidentifier</c>/<c>uuid</c> → <c>uuid</c>; <c>decimal(p,s)</c>/<c>numeric(p,s)</c> → <c>numeric(p,s)</c></item>
/// <item><c>float</c>/<c>double precision</c> → <c>double</c>, <c>real</c> → <c>single</c>; <c>varbinary</c>/<c>bytea</c> → <c>binary</c></item>
/// <item><c>vector(n)</c> → <c>vector(n)</c>; SQL Server <c>rowversion</c> → <c>rowversion</c> (Postgres uses the xmin system column)</item>
/// </list>
/// Anything else is reported as <c>other:&lt;native type&gt;</c>, so it can only match the same native type.
/// </summary>
public sealed class SchemaSnapshot
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
    public const string RowVersionCategory = "rowversion";

    private SchemaSnapshot(
        IReadOnlyDictionary<string, SchemaTable> tables,
        IReadOnlyDictionary<string, SchemaView> views,
        IReadOnlyList<SchemaForeignKey> foreignKeys)
    {
        Tables = tables;
        Views = views;
        ForeignKeys = foreignKeys;
    }

    public IReadOnlyDictionary<string, SchemaTable> Tables { get; }

    public IReadOnlyDictionary<string, SchemaView> Views { get; }

    public IReadOnlyList<SchemaForeignKey> ForeignKeys { get; }

    public static async Task<SchemaSnapshot> ReadAsync(DbConnection connection, DatabaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var catalog = provider switch
        {
            DatabaseProvider.SqlServer => SqlServerCatalog,
            DatabaseProvider.Postgres => PostgresCatalog,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

        var columns = await QueryAsync(connection, catalog.ColumnsSql, r => new
        {
            IsView = r.GetString(0).Trim() is "V" or "v",
            Table = r.GetString(1),
            Column = new SchemaColumn(r.GetString(2), r.GetBoolean(3), catalog.TypeCategory(r)),
        });

        var indexColumns = await QueryAsync(connection, catalog.IndexesSql, r => new
        {
            Table = r.GetString(0),
            Index = r.GetString(1),
            IsUnique = r.GetBoolean(2),
            HasFilter = r.GetBoolean(3),
            IsIncluded = r.GetBoolean(4),
            Column = r.IsDBNull(5) ? "(expression)" : r.GetString(5),
        });

        var foreignKeyColumns = await QueryAsync(connection, catalog.ForeignKeysSql, r => new
        {
            Name = r.GetString(0),
            Table = r.GetString(1),
            PrincipalTable = r.GetString(2),
            DeleteRule = catalog.DeleteRule(r.GetString(3)),
            Column = r.GetString(4),
            PrincipalColumn = r.GetString(5),
        });

        var indexesByTable = indexColumns
            .GroupBy(c => (c.Table, c.Index))
            .Select(g => (g.Key.Table, Index: new SchemaIndex(
                g.Key.Index,
                g.Where(c => !c.IsIncluded).Select(c => c.Column).ToList(),
                g.Where(c => c.IsIncluded).Select(c => c.Column).ToList(),
                g.First().IsUnique,
                g.First().HasFilter)))
            .ToLookup(x => x.Table, x => x.Index, StringComparer.Ordinal);

        var tables = columns
            .Where(c => !c.IsView && c.Table != MigrationsHistoryTable)
            .GroupBy(c => c.Table, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new SchemaTable(g.Key, g.Select(c => c.Column).ToList(), indexesByTable[g.Key].ToList()),
                StringComparer.Ordinal);

        var views = columns
            .Where(c => c.IsView)
            .GroupBy(c => c.Table, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new SchemaView(g.Key, g.Select(c => c.Column).ToList()), StringComparer.Ordinal);

        var foreignKeys = foreignKeyColumns
            .GroupBy(c => (c.Table, c.Name))
            .Select(g => new SchemaForeignKey(
                g.Key.Name,
                g.Key.Table,
                g.Select(c => c.Column).ToList(),
                g.First().PrincipalTable,
                g.Select(c => c.PrincipalColumn).ToList(),
                g.First().DeleteRule))
            .ToList();

        return new SchemaSnapshot(tables, views, foreignKeys);
    }

    private static async Task<List<T>> QueryAsync<T>(DbConnection connection, string sql, Func<DbDataReader, T> map)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }
        return rows;
    }

    private sealed record Catalog(
        string ColumnsSql,
        Func<DbDataReader, string> TypeCategory,
        string IndexesSql,
        string ForeignKeysSql,
        Func<string, string> DeleteRule);

    // Rows are ordered so that index and foreign key columns come in key order.
    private static readonly Catalog SqlServerCatalog = new(
        ColumnsSql: """
            SELECT o.type, o.name, c.name, c.is_nullable, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale
            FROM sys.objects o
            JOIN sys.columns c ON c.object_id = o.object_id
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND SCHEMA_NAME(o.schema_id) = 'dbo'
            ORDER BY o.name, c.column_id
            """,
        TypeCategory: SqlServerTypeCategory,
        IndexesSql: """
            SELECT t.name, i.name, i.is_unique, i.has_filter, ic.is_included_column, c.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.type > 0 AND i.is_hypothetical = 0 AND SCHEMA_NAME(t.schema_id) = 'dbo'
              AND t.name <> '__EFMigrationsHistory'
            ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """,
        ForeignKeysSql: """
            SELECT fk.name, tp.name, tr.name, fk.delete_referential_action_desc, cp.name, cr.name
            FROM sys.foreign_keys fk
            JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
            JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
            JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
            WHERE SCHEMA_NAME(fk.schema_id) = 'dbo'
            ORDER BY tp.name, fk.name, fkc.constraint_column_id
            """,
        DeleteRule: rule => rule.Replace('_', ' '));

    private static readonly Catalog PostgresCatalog = new(
        ColumnsSql: """
            SELECT c.relkind::text, c.relname, a.attname, NOT a.attnotnull, format_type(a.atttypid, a.atttypmod)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'v') AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY c.relname, a.attnum
            """,
        TypeCategory: PostgresTypeCategory,
        IndexesSql: """
            SELECT t.relname, i.relname, ix.indisunique, ix.indpred IS NOT NULL, k.ord > ix.indnkeyatts, a.attname
            FROM pg_index ix
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            CROSS JOIN LATERAL unnest(ix.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
            LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE n.nspname = 'public' AND t.relkind = 'r' AND t.relname <> '__EFMigrationsHistory'
            ORDER BY t.relname, i.relname, k.ord
            """,
        ForeignKeysSql: """
            SELECT con.conname, t.relname, rt.relname, con.confdeltype::text, a.attname, ra.attname
            FROM pg_constraint con
            JOIN pg_class t ON t.oid = con.conrelid
            JOIN pg_class rt ON rt.oid = con.confrelid
            JOIN pg_namespace n ON n.oid = con.connamespace
            CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, refattnum, ord)
            JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
            JOIN pg_attribute ra ON ra.attrelid = con.confrelid AND ra.attnum = k.refattnum
            WHERE con.contype = 'f' AND n.nspname = 'public'
            ORDER BY t.relname, con.conname, k.ord
            """,
        DeleteRule: rule => rule switch
        {
            "a" => "NO ACTION",
            "r" => "RESTRICT",
            "c" => "CASCADE",
            "n" => "SET NULL",
            "d" => "SET DEFAULT",
            _ => $"other:{rule}",
        });

    // Columns: type, table, column, is_nullable, type name, max_length (bytes), precision, scale.
    private static string SqlServerTypeCategory(DbDataReader r)
    {
        var type = r.GetString(4);
        var maxLength = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture);
        var precision = Convert.ToInt32(r.GetValue(6), CultureInfo.InvariantCulture);
        var scale = Convert.ToInt32(r.GetValue(7), CultureInfo.InvariantCulture);

        return type switch
        {
            "nvarchar" => Text(maxLength == -1 ? null : maxLength / 2),
            "varchar" => Text(maxLength == -1 ? null : maxLength),
            "tinyint" or "smallint" => "int16",
            "int" => "int32",
            "bigint" => "int64",
            "bit" => "boolean",
            "datetime2" => "timestamp",
            "uniqueidentifier" => "uuid",
            "decimal" or "numeric" => $"numeric({precision},{scale})",
            "float" when precision == 53 => "double",
            "real" => "single",
            "varbinary" => "binary",
            // vector(n) stores n float32 values after an 8-byte header.
            "vector" => $"vector({(maxLength - 8) / 4})",
            "timestamp" => RowVersionCategory,
            _ => $"other:{type}({maxLength},{precision},{scale})",
        };
    }

    private static readonly Regex PostgresType = new(
        @"^(?<name>[a-z ]+?)(?:\((?<args>[^)]*)\))?(?<suffix> with(?:out)? time zone)?$",
        RegexOptions.CultureInvariant);

    // Columns: relkind, table, column, is_nullable, format_type.
    private static string PostgresTypeCategory(DbDataReader r)
    {
        var formatted = r.GetString(4);
        var match = PostgresType.Match(formatted);
        if (!match.Success)
        {
            return $"other:{formatted}";
        }

        var name = match.Groups["name"].Value + match.Groups["suffix"].Value;
        var args = match.Groups["args"].Success ? match.Groups["args"].Value.Replace(" ", "") : null;

        return name switch
        {
            "character varying" => Text(args is null ? null : int.Parse(args, CultureInfo.InvariantCulture)),
            "text" => Text(null),
            "smallint" => "int16",
            "integer" => "int32",
            "bigint" => "int64",
            "boolean" => "boolean",
            "timestamp with time zone" => "timestamp",
            "uuid" => "uuid",
            "numeric" when args is not null => $"numeric({args})",
            "double precision" => "double",
            "real" => "single",
            "bytea" => "binary",
            "vector" when args is not null => $"vector({args})",
            _ => $"other:{formatted}",
        };
    }

    private static string Text(int? maxLength) => maxLength is null ? "text(max)" : $"text({maxLength})";
}
