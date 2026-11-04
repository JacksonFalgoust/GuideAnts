using System.Data;
using System.Data.Common;
using System.Globalization;
using GuideAntsApi.DataModel.Database;

namespace GuideAntsApi.DatabaseParity.Tests.Infrastructure;

/// <summary>
/// Reads table rows with every value reduced to a provider-neutral string, so a SQL Server row and a Postgres row
/// with the same data produce the same strings:
/// <list type="bullet">
/// <item><c>NULL</c> for a database null</item>
/// <item>a timestamp is only <see cref="Timestamp"/>: seeded timestamps come from <c>GETUTCDATE()</c>/<c>now()</c> at migrate time</item>
/// <item>a uuid in lowercase <c>D</c> format, a boolean as <c>true</c>/<c>false</c>, bytes as <c>0x</c> hex</item>
/// <item>integers of any width as decimal digits, decimals without trailing zeros, doubles round-trippable</item>
/// <item>text quoted and otherwise unchanged (compared ordinally)</item>
/// </list>
/// Any other value type throws, so a column type this reader does not know can't compare as equal by accident.
/// </summary>
public static class TableRows
{
    public const string Null = "NULL";
    public const string Timestamp = "(timestamp)";

    /// <summary>Primary key columns of every table in the default schema, in key order.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> PrimaryKeysAsync(
        DbConnection connection, DatabaseProvider provider)
    {
        var sql = provider switch
        {
            DatabaseProvider.SqlServer => """
                SELECT t.name, c.name
                FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.is_primary_key = 1 AND SCHEMA_NAME(t.schema_id) = 'dbo'
                ORDER BY t.name, ic.key_ordinal
                """,
            DatabaseProvider.Postgres => """
                SELECT t.relname, a.attname
                FROM pg_constraint con
                JOIN pg_class t ON t.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                WHERE con.contype = 'p' AND n.nspname = 'public'
                ORDER BY t.relname, k.ord
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

        await OpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (!keys.TryGetValue(table, out var columns))
            {
                keys[table] = columns = new List<string>();
            }
            columns.Add(reader.GetString(1));
        }
        return keys.ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value, StringComparer.Ordinal);
    }

    /// <summary>All rows of <paramref name="table"/>, ordered by <paramref name="orderBy"/>, one normalized value per column.</summary>
    public static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        DbConnection connection,
        DatabaseProvider provider,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> orderBy)
    {
        Func<string, string> quote = provider switch
        {
            DatabaseProvider.SqlServer => name => $"[{name.Replace("]", "]]")}]",
            DatabaseProvider.Postgres => name => $"\"{name.Replace("\"", "\"\"")}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

        await OpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", columns.Select(quote))} FROM {quote(table)} ORDER BY {string.Join(", ", orderBy.Select(quote))}";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<IReadOnlyList<string>>();
        while (await reader.ReadAsync())
        {
            var row = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                row[i] = Normalize(reader.GetValue(i), $"{table}.{columns[i]}");
            }
            rows.Add(row);
        }
        return rows;
    }

    public static string Normalize(object? value, string column) => value switch
    {
        null or DBNull => Null,
        DateTime or DateTimeOffset => Timestamp,
        string text => $"'{text}'",
        Guid guid => guid.ToString("D"),
        bool flag => flag ? "true" : "false",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        byte or sbyte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"{column}: no provider-neutral form for {value.GetType().FullName}."),
    };

    private static async Task OpenAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
