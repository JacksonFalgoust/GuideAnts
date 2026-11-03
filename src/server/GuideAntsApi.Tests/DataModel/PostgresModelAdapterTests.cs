using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// The Postgres model is the SQL Server model rewritten by <see cref="PostgresModelAdapter"/>.
/// These tests read the design-time model only; no database connection is opened.
/// </summary>
[TestClass]
public sealed class PostgresModelAdapterTests
{
    private const int MaxIdentifierLength = 63;

    private static readonly Lazy<IModel> PostgresModel = new(() => BuildModel(
        DatabaseProvider.Postgres, "Host=localhost;Database=model_only"));

    private static readonly Lazy<IModel> SqlServerModel = new(() => BuildModel(
        DatabaseProvider.SqlServer, "Server=localhost;Database=model_only"));

    private static IModel BuildModel(DatabaseProvider provider, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(builder, provider, connectionString, new DbProviderTuning());
        using var context = new ApplicationDbContext(builder.Options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IEnumerable<IProperty> AllProperties(IModel model)
        => model.GetEntityTypes().SelectMany(e => e.GetDeclaredProperties());

    private static IProperty RowVersionOf<TEntity>(IModel model)
        => model.FindEntityType(typeof(TEntity))!.FindProperty("RowVersion")!;

    [TestMethod]
    public void No_sqlserver_column_types_remain()
    {
        var sqlServerTypes = new[] { "nvarchar", "varbinary", "datetime2", "uniqueidentifier" };

        var offenders = AllProperties(PostgresModel.Value)
            .Select(p => (Property: $"{p.DeclaringType.DisplayName()}.{p.Name}", Type: p.GetColumnType()))
            .Where(x => sqlServerTypes.Any(t => x.Type.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty();
        PostgresModel.Value.FindEntityType(typeof(ApplicationSetting))!
            .FindProperty(nameof(ApplicationSetting.JsonValue))!.GetColumnType().Should().Be("text");
        PostgresModel.Value.FindEntityType(typeof(Assistant))!
            .FindProperty(nameof(Assistant.AvatarImageBytes))!.GetColumnType().Should().Be("bytea");
    }

    [TestMethod]
    public void No_sqlserver_default_sql_remains()
    {
        var postgres = PostgresModel.Value;

        AllProperties(postgres)
            .Select(p => p.GetDefaultValueSql())
            .Where(sql => sql is not null)
            .Should().NotContain(sql => sql!.Contains("GETUTCDATE", StringComparison.OrdinalIgnoreCase)
                                     || sql.Contains("NEWID", StringComparison.OrdinalIgnoreCase));

        // Every SQL Server default has its Postgres counterpart on the same property.
        var sqlServerDefaults = AllProperties(SqlServerModel.Value)
            .Where(p => p.GetDefaultValueSql() is "GETUTCDATE()" or "NEWID()")
            .ToList();
        sqlServerDefaults.Count(p => p.GetDefaultValueSql() == "GETUTCDATE()").Should().BeGreaterThanOrEqualTo(12);
        sqlServerDefaults.Should().Contain(p => p.GetDefaultValueSql() == "NEWID()");

        foreach (var sqlServerProperty in sqlServerDefaults)
        {
            var postgresProperty = postgres.FindEntityType(sqlServerProperty.DeclaringType.Name)!
                .FindProperty(sqlServerProperty.Name)!;
            var expected = sqlServerProperty.GetDefaultValueSql() == "NEWID()" ? "gen_random_uuid()" : "now()";
            postgresProperty.GetDefaultValueSql().Should().Be(expected,
                $"{sqlServerProperty.DeclaringType.DisplayName()}.{sqlServerProperty.Name} defaults to {sqlServerProperty.GetDefaultValueSql()} on SQL Server");
        }
    }

    [TestMethod]
    public void Index_filters_use_postgres_quoting()
    {
        var filters = PostgresModel.Value.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredIndexes())
            .Select(i => i.GetFilter())
            .Where(f => f is not null)
            .ToList();

        filters.Should().NotBeEmpty();
        filters.Should().NotContain(f => f!.Contains('[') || f.Contains(']'));

        var dedup = PostgresModel.Value.FindEntityType(typeof(JobQueue))!.GetDeclaredIndexes()
            .Single(i => i.GetDatabaseName() == "IX_JobQueue_CorrelationDedup");
        dedup.GetFilter().Should().Be("\"CorrelationId\" IS NOT NULL AND \"Status\" IN (0, 2)");

        var friendlyName = PostgresModel.Value.FindEntityType(typeof(PublishedGuide))!.GetDeclaredIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(PublishedGuide.FriendlyName) }));
        friendlyName.GetFilter().Should().Be("\"FriendlyName\" IS NOT NULL");
    }

    [TestMethod]
    public void Check_constraints_use_postgres_quoting()
    {
        var model = PostgresModel.Value;
        var constraints = model.GetEntityTypes().SelectMany(e => e.GetDeclaredCheckConstraints()).ToList();

        constraints.Should().NotBeEmpty();
        constraints.Should().NotContain(c => c.Sql.Contains('[') || c.Sql.Contains(']'));

        constraints.Single(c => c.ModelName == "CK_ExpiresInFuture").Sql
            .Should().Be("\"ExpiresAt\" > \"LockedAt\"");
        constraints.Single(c => c.ModelName == "CK_FileLineageEvent_NotebookId").Sql
            .Should().Be("\"FileKind\" = 0 OR (\"FileKind\" = 1 AND \"NotebookId\" IS NOT NULL)");
        constraints.Single(c => c.ModelName == "CK_Turn_Status").Sql
            .Should().Be("\"Status\" IN ('streaming', 'completed', 'cancelled', 'timed_out', 'failed', 'interrupted', 'pending_client_tool')");
    }

    [TestMethod]
    public void Every_string_column_uses_guideants_ci()
    {
        var model = PostgresModel.Value;

        var stringProperties = AllProperties(model)
            .Where(p => (p.GetTypeMapping().Converter?.ProviderClrType ?? p.ClrType) == typeof(string))
            .ToList();

        stringProperties.Should().NotBeEmpty();
        stringProperties
            .Where(p => p.GetCollation() != PostgresModelAdapter.CaseInsensitiveCollation)
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}")
            .Should().BeEmpty();

        PostgresModelAdapter.CaseInsensitiveCollation.Should().Be("guideants_ci");
        var collation = model.GetCollations().Single(c => c.Name == "guideants_ci");
        collation.LcCollate.Should().Be("und-u-ks-level2");
        collation.LcCtype.Should().Be("und-u-ks-level2");
        collation.Provider.Should().Be("icu");
        collation.IsDeterministic.Should().BeFalse();
    }

    [TestMethod]
    public void Row_versions_map_to_xmin()
    {
        var model = PostgresModel.Value;
        var rowVersions = new[]
        {
            RowVersionOf<ApplicationSetting>(model),
            RowVersionOf<JobQueue>(model),
            RowVersionOf<LocalModelOperation>(model),
            RowVersionOf<LocalModelInstallation>(model),
            RowVersionOf<ProjectScheduledJob>(model),
        };

        foreach (var property in rowVersions)
        {
            property.ClrType.Should().Be(typeof(byte[]));
            property.GetColumnName().Should().Be("xmin");
            property.GetColumnType().Should().Be("xid");
            property.IsConcurrencyToken.Should().BeTrue();
            property.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);

            var converter = property.GetValueConverter();
            converter.Should().NotBeNull();
            converter!.ModelClrType.Should().Be(typeof(byte[]));
            converter.ProviderClrType.Should().Be(typeof(uint));
        }

        // No other byte[] row version is left behind.
        AllProperties(model)
            .Where(p => p.ClrType == typeof(byte[]) && p.IsConcurrencyToken && p.ValueGenerated == ValueGenerated.OnAddOrUpdate)
            .Should().HaveCount(rowVersions.Length);
    }

    [TestMethod]
    public void Row_version_converter_round_trips_four_bytes()
    {
        var converter = RowVersionOf<ApplicationSetting>(PostgresModel.Value).GetValueConverter()!;

        foreach (var xmin in new uint[] { 0, 1, 742, 0x01020304, uint.MaxValue })
        {
            var bytes = (byte[])converter.ConvertFromProvider(xmin)!;
            bytes.Should().HaveCount(4);
            converter.ConvertToProvider(bytes).Should().Be(xmin);
        }

        ((byte[])converter.ConvertFromProvider(0x01020304u)!).Should().Equal(1, 2, 3, 4);
    }

    [TestMethod]
    public void Embedding_maps_to_pgvector()
    {
        var embedding = PostgresModel.Value.FindEntityType(typeof(DocumentChunk))!
            .FindProperty(nameof(DocumentChunk.Embedding))!;

        embedding.GetColumnType().Should().Be("vector(1536)");
        var converter = embedding.GetValueConverter();
        converter.Should().NotBeNull();
        converter!.ModelClrType.Should().Be(typeof(float[]));
        converter.ProviderClrType.Should().Be(typeof(Pgvector.Vector));

        // Change tracking compares and snapshots the array element by element, as on SQL Server.
        var sqlServerEmbedding = SqlServerModel.Value.FindEntityType(typeof(DocumentChunk))!
            .FindProperty(nameof(DocumentChunk.Embedding))!;
        embedding.GetValueComparer().GetType().Should().Be(sqlServerEmbedding.GetValueComparer().GetType());
        var original = new[] { 1f, 2f };
        embedding.GetValueComparer().Snapshot(original).Should().NotBeSameAs(original);

        var vector = (Pgvector.Vector)converter.ConvertToProvider(new[] { 1f, 2f, 3f })!;
        vector.ToArray().Should().Equal(1f, 2f, 3f);
        ((float[])converter.ConvertFromProvider(vector)!).Should().Equal(1f, 2f, 3f);
    }

    [TestMethod]
    public void Vector_extension_is_declared()
    {
        PostgresModel.Value.GetPostgresExtensions().Should().Contain(e => e.Name == "vector");
    }

    [TestMethod]
    public void Covering_indexes_are_carried_over()
    {
        var postgres = PostgresModel.Value;

        var costLimits = postgres.FindEntityType(typeof(UsageEvent))!.GetDeclaredIndexes()
            .Single(i => i.GetDatabaseName() == "IX_UsageEvents_NotebookId_Created_ForCostLimits");
        NpgsqlIndexExtensions.GetIncludeProperties(costLimits).Should().Equal(nameof(UsageEvent.ChargeUsd));

        // Every SQL Server covering index has the same INCLUDE columns on Postgres.
        var sqlServerCovering = SqlServerModel.Value.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredIndexes())
            .Where(i => Microsoft.EntityFrameworkCore.SqlServerIndexExtensions.GetIncludeProperties(i) is { Count: > 0 })
            .ToList();
        sqlServerCovering.Should().HaveCount(8);

        foreach (var sqlServerIndex in sqlServerCovering)
        {
            var postgresIndex = postgres.FindEntityType(sqlServerIndex.DeclaringEntityType.Name)!
                .FindIndex(sqlServerIndex.Properties
                    .Select(p => postgres.FindEntityType(sqlServerIndex.DeclaringEntityType.Name)!.FindProperty(p.Name)!)
                    .ToList())!;

            NpgsqlIndexExtensions.GetIncludeProperties(postgresIndex)
                .Should().Equal(Microsoft.EntityFrameworkCore.SqlServerIndexExtensions.GetIncludeProperties(sqlServerIndex));
        }

        // The SQL Server annotation is not left in the Postgres model.
        postgres.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredIndexes())
            .Where(i => Microsoft.EntityFrameworkCore.SqlServerIndexExtensions.GetIncludeProperties(i) is not null)
            .Should().BeEmpty();
    }

    [TestMethod]
    public void All_identifiers_fit_in_63_chars()
    {
        var model = PostgresModel.Value;
        var names = new List<(string Kind, string Name)>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is not null)
            {
                names.Add(("table", table));
            }
            var view = entityType.GetViewName();
            if (view is not null)
            {
                names.Add(("view", view));
            }

            names.AddRange(entityType.GetDeclaredProperties().Select(p => ("column", p.GetColumnName())));
            names.AddRange(entityType.GetDeclaredKeys().Select(k => ("key", k.GetName()!)));
            names.AddRange(entityType.GetDeclaredIndexes().Select(i => ("index", i.GetDatabaseName()!)));
            names.AddRange(entityType.GetDeclaredForeignKeys().Select(f => ("foreign key", f.GetConstraintName()!)));
            names.AddRange(entityType.GetDeclaredCheckConstraints().Select(c => ("check constraint", c.Name!)));
        }

        names.Where(n => n.Name.Length > MaxIdentifierLength)
            .Select(n => $"{n.Kind} {n.Name} ({n.Name.Length})")
            .Should().BeEmpty();

        // Long names are shortened by the adapter, not cut by EF's '~' truncation.
        names.Where(n => n.Name.Contains('~')).Select(n => n.Name).Should().BeEmpty();

        // Tables, indexes and index-backed constraints share one namespace per schema in Postgres.
        var relationNames = names.Where(n => n.Kind is "table" or "index" or "key").Select(n => n.Name).ToList();
        relationNames.Should().OnlyHaveUniqueItems();

        // The conversation lock code matches this constraint name, so it must survive unchanged.
        names.Should().Contain(("foreign key", "FK_ConversationLocks_NotebookConversations_ConversationId"));
    }

    [TestMethod]
    public void Long_names_are_shortened_deterministically()
    {
        const string name = "IX_HostFolderMountLinks_NotebookId_LinkRelativePath_ActiveUnique";
        name.Length.Should().BeGreaterThan(MaxIdentifierLength);

        var shortened = PostgresModelAdapter.ShortenIdentifier(name);
        shortened.Length.Should().BeLessThanOrEqualTo(MaxIdentifierLength);
        shortened.Should().Be(PostgresModelAdapter.ShortenIdentifier(name));
        shortened.Should().StartWith("IX_HostFolderMountLinks_NotebookId_LinkRelativePath");
        PostgresModelAdapter.ShortenIdentifier(name + "2").Should().NotBe(shortened);

        PostgresModelAdapter.ShortenIdentifier("FK_ConversationLocks_NotebookConversations_ConversationId")
            .Should().Be("FK_ConversationLocks_NotebookConversations_ConversationId");

        PostgresModel.Value.FindEntityType(typeof(HostFolderMountLink))!.GetDeclaredIndexes()
            .Select(i => i.GetDatabaseName())
            .Should().Contain(shortened);
    }

    [TestMethod]
    public void SqlServer_model_is_untouched()
    {
        var model = SqlServerModel.Value;

        model.FindEntityType(typeof(ApplicationSetting))!
            .FindProperty(nameof(ApplicationSetting.JsonValue))!.GetColumnType().Should().Be("nvarchar(max)");

        model.FindEntityType(typeof(PublishedGuide))!.GetDeclaredIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(PublishedGuide.FriendlyName) }))
            .GetFilter().Should().Be("[FriendlyName] IS NOT NULL");

        var rowVersion = RowVersionOf<ApplicationSetting>(model);
        rowVersion.GetColumnName().Should().Be("RowVersion");
        rowVersion.GetColumnType().Should().Be("rowversion");
        rowVersion.GetValueConverter().Should().BeNull();

        model.GetCollations().Should().BeEmpty();
        AllProperties(model).Should().NotContain(p => p.GetCollation() != null);
        model.FindEntityType(typeof(HostFolderMountLink))!.GetDeclaredIndexes()
            .Select(i => i.GetDatabaseName())
            .Should().Contain("IX_HostFolderMountLinks_NotebookId_LinkRelativePath_ActiveUnique");
    }
}
