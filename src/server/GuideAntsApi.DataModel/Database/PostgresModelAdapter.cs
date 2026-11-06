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

    // SQL Server stores a longer nvarchar/varchar as (max), and bounds an unbounded string that is part of a
    // key, foreign key or index by its 900-byte index key limit.
    private const int SqlServerMaxUnicodeLength = 4000;
    private const int SqlServerMaxAnsiLength = 8000;
    private const int SqlServerKeyUnicodeLength = 450;
    private const int SqlServerKeyAnsiLength = 900;

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

    /// <summary>
    /// SQL Server compares and stores a DateTime's raw ticks whatever its <see cref="DateTimeKind"/>; Npgsql refuses a
    /// non-UTC DateTime for <c>timestamp with time zone</c>. Every value is marked UTC on the way in (never converted:
    /// the store holds UTC instants), and cut to the whole microseconds Postgres keeps, so a parameter compares with
    /// what was stored. Values read back are UTC.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> DateTimeToStored = new(
        value => ToStoredDateTime(value),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>
    /// Postgres <c>text</c> can't hold U+0000 (SQL Server's <c>nvarchar</c> can), so it is stored as U+FFFD, the
    /// Unicode replacement character. Every other string is stored as it is; values are read back unchanged.
    /// </summary>
    private static readonly ValueConverter<string, string> StringToStored = new(
        value => ReplaceNulCharacters(value),
        value => value);

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

                foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
                {
                    // SQL Server has no ON DELETE RESTRICT and creates these as NO ACTION. EF treats the two
                    // the same when saving, so this only makes the Postgres constraint match.
                    if (foreignKey.DeleteBehavior == DeleteBehavior.Restrict)
                    {
                        foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
                    }
                }
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

    /// <summary>
    /// Cuts <paramref name="value"/> to the whole microseconds a Postgres <c>timestamp</c> keeps, keeping its Kind.
    /// <see cref="DateTime.MaxValue"/> is left alone: Npgsql stores it as <c>infinity</c> and reads that back as
    /// <see cref="DateTime.MaxValue"/>, which a cut value would no longer be.
    /// </summary>
    public static DateTime TruncateToMicroseconds(DateTime value)
        => value == DateTime.MaxValue
            ? value
            : new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, value.Kind);

    /// <summary>The value written for a DateTime on Postgres: whole microseconds, marked UTC without conversion.</summary>
    public static DateTime ToStoredDateTime(DateTime value)
        => DateTime.SpecifyKind(TruncateToMicroseconds(value), DateTimeKind.Utc);

    /// <summary>The value written for a string on Postgres: U+0000 becomes U+FFFD.</summary>
    public static string ReplaceNulCharacters(string value) => value.Replace('\u0000', '\uFFFD');

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
            // EF treats float[] as a primitive collection; on Postgres it is one scalar vector value, and the
            // migrations snapshot cannot declare a primitive collection of Pgvector.Vector.
            property.SetElementType(null);
            property.SetValueConverter(EmbeddingToVector);
            // Same element-wise comparison and snapshot copy that SQL Server's float[] vector mapping uses.
            property.SetValueComparer(EmbeddingComparer);
        }

        if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime) && property.GetValueConverter() is null)
        {
            property.SetValueConverter(DateTimeToStored);
        }

        if (property.ClrType == typeof(string) && property.GetValueConverter() is null)
        {
            property.SetValueConverter(StringToStored);
        }

        var providerType = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType;
        if (providerType == typeof(string))
        {
            MatchSqlServerStringLength(property);
            property.SetCollation(CaseInsensitiveCollation);
        }

        if (property.GetColumnName() is { Length: > MaxIdentifierLength } column)
        {
            property.SetColumnName(ShortenIdentifier(column));
        }
    }

    /// <summary>
    /// Gives a string column the length SQL Server's type mapping gives it, so both databases accept the same values:
    /// an unbounded string in a key or index is bounded at 450 (900 if not Unicode), and a length over 4000
    /// (8000 if not Unicode) is unbounded. Facets come from the principal key for a foreign key, as in EF's mapping.
    /// </summary>
    private static void MatchSqlServerStringLength(IMutableProperty property)
    {
        // An explicit store type (here or on the principal key it copies) is mapped elsewhere or kept as declared.
        var principals = property.GetPrincipals();
        if (principals.Any(p => p.FindAnnotation(RelationalAnnotationNames.ColumnType) is not null) || property.IsFixedLength() == true)
        {
            return;
        }

        var declared = principals.Select(p => p.GetMaxLength()).FirstOrDefault(l => l is not null);
        var unicode = principals.Select(p => p.IsUnicode()).FirstOrDefault(u => u is not null) ?? true;

        var length = declared;
        if (length is null && (property.IsKey() || property.IsForeignKey() || property.IsIndex()))
        {
            length = unicode ? SqlServerKeyUnicodeLength : SqlServerKeyAnsiLength;
        }
        if (length > (unicode ? SqlServerMaxUnicodeLength : SqlServerMaxAnsiLength))
        {
            length = null;
        }

        if (length != declared)
        {
            property.SetColumnType(length is null ? "text" : $"character varying({length})");
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
