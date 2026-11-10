using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.Services.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace GuideAntsApi.Tests.Services.Migrations;

/// <summary>
/// The storage migration runners' own context (parity report #20). On SQL Server it must be exactly the context the
/// runners built before Postgres support; on Postgres it must be the host's Postgres configuration without the
/// retrying strategy, because the runners open their own transactions.
/// </summary>
[TestClass]
public sealed class StorageMigrationDbContextTests
{
    private const string SqlServerConnectionString = "Server=localhost;Database=storage_migration;";
    private const string PostgresConnectionString = "Host=localhost;Database=storage_migration";

    [TestMethod]
    public void SqlServer_options_equal_the_runners_original_inline_options()
    {
        // The configuration both runners used before they took a provider.
        var original = new DbContextOptionsBuilder<ApplicationDbContext>();
        original.UseSqlServer(SqlServerConnectionString, sql =>
        {
            sql.UseVectorSearch();
            sql.CommandTimeout(120);
        });

        var options = StorageMigrationDbContext.CreateOptions(DatabaseProvider.SqlServer, SqlServerConnectionString);

        var expected = original.Options.Extensions.ToList();
        var actual = options.Extensions.ToList();
        actual.Select(e => e.GetType()).Should().Equal(expected.Select(e => e.GetType()));
        for (var i = 0; i < expected.Count; i++)
        {
            actual[i].Info.LogFragment.Should().Be(expected[i].Info.LogFragment, actual[i].GetType().Name);
            actual[i].Info.GetServiceProviderHashCode().Should().Be(expected[i].Info.GetServiceProviderHashCode(), actual[i].GetType().Name);
            actual[i].Info.ShouldUseSameServiceProvider(expected[i].Info).Should().BeTrue(actual[i].GetType().Name);
        }

        var sql = options.FindExtension<SqlServerOptionsExtension>()!;
        sql.ConnectionString.Should().Be(SqlServerConnectionString);
        sql.CommandTimeout.Should().Be(120);
        sql.ExecutionStrategyFactory.Should().BeNull();
        sql.QuerySplittingBehavior.Should().BeNull();
        options.FindExtension<CoreOptionsExtension>()?.ReplacedServices.Should().BeNull();
    }

    [TestMethod]
    public void Postgres_options_use_npgsql_with_the_host_model_settings_and_no_retrying_strategy()
    {
        var options = StorageMigrationDbContext.CreateOptions(DatabaseProvider.Postgres, PostgresConnectionString);

        options.FindExtension<SqlServerOptionsExtension>().Should().BeNull();
        var npgsql = options.FindExtension<NpgsqlOptionsExtension>()!;
        npgsql.Should().NotBeNull();
        npgsql.ConnectionString.Should().Be(PostgresConnectionString);
        npgsql.CommandTimeout.Should().Be(120);
        npgsql.ExecutionStrategyFactory.Should().BeNull();
        npgsql.MigrationsAssembly.Should().Be(ApplicationDbContextOptions.PostgresMigrationsAssembly);
        options.FindExtension<CoreOptionsExtension>()!.ReplacedServices!.Values
            .Should().Contain(typeof(SqlServerNullOrderingQuerySqlGeneratorFactory));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.Postgres)]
    public void Runner_context_does_not_retry_so_it_can_open_its_own_transactions(DatabaseProvider provider)
    {
        using var runnerContext = StorageMigrationDbContext.Create(provider, ConnectionString(provider));
        runnerContext.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();

        // The host's default tuning still retries.
        var hostBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(hostBuilder, provider, ConnectionString(provider), new DbProviderTuning(60, true, true));
        using var hostContext = new ApplicationDbContext(hostBuilder.Options);
        hostContext.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.Postgres)]
    public void Runner_context_maps_the_model_for_its_provider(DatabaseProvider provider)
    {
        using var context = StorageMigrationDbContext.Create(provider, ConnectionString(provider));

        context.Database.IsNpgsql().Should().Be(provider == DatabaseProvider.Postgres);
        context.Database.IsSqlServer().Should().Be(provider == DatabaseProvider.SqlServer);
        var sql = context.Projects.Where(p => p.Slug.StartsWith("a")).ToQueryString();
        sql.Should().Contain(provider == DatabaseProvider.Postgres ? "\"Projects\"" : "[Projects]");
    }

    private static string ConnectionString(DatabaseProvider provider)
        => provider == DatabaseProvider.Postgres ? PostgresConnectionString : SqlServerConnectionString;
}
