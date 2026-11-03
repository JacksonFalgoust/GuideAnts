using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace GuideAntsApi.DataModel.Database;

/// <summary>
/// Rewrites the SQL Server-only details of the single GuideAnts model for Postgres.
/// <see cref="ApplicationDbContext"/> calls <see cref="Apply"/> as the last step of
/// <c>OnModelCreating</c>, and only when the provider is Npgsql, so the SQL Server model is unchanged.
/// </summary>
public static class PostgresModelAdapter
{
    /// <summary>Case-insensitive ICU collation applied to every string column (SQL Server's default collation ignores case).</summary>
    public const string CaseInsensitiveCollation = "guideants_ci";

    /// <summary>Postgres truncates identifiers longer than this (NAMEDATALEN - 1).</summary>
    public const int MaxIdentifierLength = 63;

    private const string CaseInsensitiveLocale = "und-u-ks-level2";
    private const string SqlServerIncludeAnnotation = "SqlServer:Include";

    private static readonly Dictionary<string, string> ColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nvarchar(max)"] = "text",
        ["varbinary(max)"] = "bytea",
    };

    private static readonly Dictionary<string, string> DefaultValueSql = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GETUTCDATE()"] = "now()",
        ["NEWID()"] = "gen_random_uuid()",
    };

    /// <summary>
    /// <c>xmin</c> is a 32-bit transaction id; the model keeps SQL Server's <c>byte[]</c> row version,
    /// so it travels as 4 big-endian bytes. A token of any other length (for example a stale client value)
    /// maps to 0, which no row has, so it always reports a concurrency conflict.
    /// </summary>
    private static readonly ValueConverter<byte[], uint> RowVersionToXmin = new(
        rowVersion => ToXmin(rowVersion),
        xmin => FromXmin(xmin));

    private static readonly ValueComparer<byte[]> RowVersionComparer = new(favorStructuralComparisons: true);

    private static readonly ValueConverter<float[], Vector> EmbeddingToVector = new(
        embedding => new Vector(embedding),
        vector => vector.ToArray());

    private static readonly ValueComparer EmbeddingComparer =
        new ListOfValueTypesComparer<float[], float>(ValueComparer.CreateDefault(typeof(float), favorStructuralComparisons: false));

    /// <summary>
    /// A single-quoted literal, a double-quoted identifier, a bracketed identifier, or a bare word.
    /// Literals and quoted identifiers are matched only so they are left alone.
    /// </summary>
    private static readonly Regex SqlToken = new(
        """'(?:[^']|'')*'|"(?:[^"]|"")*"|\[(?<bracketed>[^\]]+)\]|\b(?<word>[A-Za-z_][A-Za-z0-9_]*)\b""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Apply(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Model;
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasCollation(CaseInsensitiveCollation, locale: CaseInsensitiveLocale, provider: "icu", deterministic: false);

        // EF cuts default names to the provider's 63 characters with a trailing '~' and numbers any
        // collisions in model order. Lift the limit while this pass runs so it sees every full name and
        // shortens the long ones with ShortenIdentifier, whose result depends only on the name itself.
        var maxIdentifierLength = model.FindAnnotation(RelationalAnnotationNames.MaxIdentifierLength)?.Value;
        model.RemoveAnnotation(RelationalAnnotationNames.MaxIdentifierLength);
        try
        {
            foreach (var entityType in model.GetEntityTypes())
            {
                if (entityType.GetTableName() is { Length: > MaxIdentifierLength } table)
                {
                    entityType.SetTableName(ShortenIdentifier(table));
                }

                foreach (var property in entityType.GetDeclaredProperties())
                {
                    AdaptProperty(property);
                }

                var columns = entityType.GetProperties()
                    .Select(p => p.GetColumnName())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(c => c, StringComparer.OrdinalIgnoreCase);

                foreach (var index in entityType.GetDeclaredIndexes())
                {
                    AdaptIndex(index, columns);
                }

                AdaptCheckConstraints(entityType, columns);
            }

            // Constraint and index names are built from table and column names, so they come last.
            foreach (var entityType in model.GetEntityTypes())
            {
                ShortenConstraintNames(entityType);
            }
        }
        finally
        {
            if (maxIdentifierLength is not null)
            {
                model.SetAnnotation(RelationalAnnotationNames.MaxIdentifierLength, maxIdentifierLength);
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="name"/> when it fits in 63 characters; otherwise its first 54 characters,
    /// an underscore and the first 8 hex digits of its SHA-256, so the result is stable and distinct.
    /// </summary>
    public static string ShortenIdentifier(string name)
    {
        if (name.Length <= MaxIdentifierLength)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
        return $"{name[..(MaxIdentifierLength - hash.Length - 1)]}_{hash}";
    }

    private static void AdaptProperty(IMutableProperty property)
    {
        if (property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value is string columnType
            && ColumnTypes.TryGetValue(columnType, out var postgresType))
        {
            property.SetColumnType(postgresType);
        }

        if (property.GetDefaultValueSql() is { } defaultSql
            && DefaultValueSql.TryGetValue(defaultSql.Trim(), out var postgresDefault))
        {
            property.SetDefaultValueSql(postgresDefault);
        }

        if (property.ClrType == typeof(byte[])
            && property.IsConcurrencyToken
            && property.ValueGenerated == ValueGenerated.OnAddOrUpdate)
        {
            // Postgres has no rowversion; the xmin system column changes on every row update.
            property.SetValueConverter(RowVersionToXmin);
            property.SetValueComparer(RowVersionComparer);
            property.SetColumnName("xmin");
            property.SetColumnType("xid");
            property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            property.IsConcurrencyToken = true;
        }

        if (property.ClrType == typeof(float[])
            && property.GetColumnType() is { } vectorType
            && vectorType.StartsWith("vector(", StringComparison.OrdinalIgnoreCase))
        {
            property.SetValueConverter(EmbeddingToVector);
            // Same element-wise comparison and snapshot copy that SQL Server's float[] vector mapping uses.
            property.SetValueComparer(EmbeddingComparer);
        }

        var providerType = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType;
        if (providerType == typeof(string))
        {
            property.SetCollation(CaseInsensitiveCollation);
        }

        if (property.GetColumnName() is { Length: > MaxIdentifierLength } column)
        {
            property.SetColumnName(ShortenIdentifier(column));
        }
    }

    private static void AdaptIndex(IMutableIndex index, IReadOnlyDictionary<string, string> columns)
    {
        if (index.GetFilter() is { } filter)
        {
            index.SetFilter(ToPostgresSql(filter, columns));
        }

        // The model's covering indexes are declared with SQL Server's INCLUDE; Postgres 11+ has the same clause.
        if (Microsoft.EntityFrameworkCore.SqlServerIndexExtensions.GetIncludeProperties(index) is { Count: > 0 } include)
        {
            NpgsqlIndexExtensions.SetIncludeProperties(index, include);
        }
        index.RemoveAnnotation(SqlServerIncludeAnnotation);
    }

    private static void AdaptCheckConstraints(IMutableEntityType entityType, IReadOnlyDictionary<string, string> columns)
    {
        foreach (var constraint in entityType.GetDeclaredCheckConstraints().ToList())
        {
            var sql = ToPostgresSql(constraint.Sql, columns);
            if (sql == constraint.Sql)
            {
                continue;
            }

            // A check constraint's SQL is fixed once added, so replace the constraint.
            var modelName = constraint.ModelName;
            var name = constraint.Name;
            entityType.RemoveCheckConstraint(modelName);
            var replacement = entityType.AddCheckConstraint(modelName, sql);
            if (replacement.Name != name)
            {
                replacement.Name = name;
            }
        }
    }

    private static void ShortenConstraintNames(IMutableEntityType entityType)
    {
        foreach (var key in entityType.GetDeclaredKeys())
        {
            if (key.GetName() is { Length: > MaxIdentifierLength } keyName)
            {
                key.SetName(ShortenIdentifier(keyName));
            }
        }

        foreach (var index in entityType.GetDeclaredIndexes())
        {
            if (index.GetDatabaseName() is { Length: > MaxIdentifierLength } indexName)
            {
                index.SetDatabaseName(ShortenIdentifier(indexName));
            }
        }

        foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
        {
            if (foreignKey.GetConstraintName() is { Length: > MaxIdentifierLength } constraintName)
            {
                foreignKey.SetConstraintName(ShortenIdentifier(constraintName));
            }
        }

        foreach (var constraint in entityType.GetDeclaredCheckConstraints())
        {
            if (constraint.Name is { Length: > MaxIdentifierLength } checkName)
            {
                constraint.Name = ShortenIdentifier(checkName);
            }
        }
    }

    /// <summary>
    /// Rewrites a SQL Server filter or check expression for Postgres: <c>[X]</c> becomes <c>"X"</c>, and a bare
    /// column name (which SQL Server resolves ignoring case, but Postgres folds to lower case) is quoted.
    /// String literals and already-quoted identifiers are left as they are.
    /// </summary>
    private static string ToPostgresSql(string sql, IReadOnlyDictionary<string, string> columns)
        => SqlToken.Replace(sql, match =>
        {
            if (match.Groups["bracketed"].Success)
            {
                var name = match.Groups["bracketed"].Value;
                return Quote(columns.TryGetValue(name, out var column) ? column : name);
            }

            if (match.Groups["word"].Success && columns.TryGetValue(match.Value, out var bareColumn))
            {
                return Quote(bareColumn);
            }

            return match.Value;
        });

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static uint ToXmin(byte[] rowVersion)
        => rowVersion.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32BigEndian(rowVersion) : 0;

    private static byte[] FromXmin(uint xmin)
    {
        var rowVersion = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(rowVersion, xmin);
        return rowVersion;
    }
}
