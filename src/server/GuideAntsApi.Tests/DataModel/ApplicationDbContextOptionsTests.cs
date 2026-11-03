using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace GuideAntsApi.Tests.DataModel;

[TestClass]
public sealed class ApplicationDbContextOptionsTests
{
    [TestMethod]
    public void SqlServer_options_match_todays_settings()
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            DatabaseProvider.SqlServer,
            "Server=localhost;Database=x;",
            new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));

        var ext = builder.Options.FindExtension<SqlServerOptionsExtension>();
        ext.Should().NotBeNull();
        ext!.CommandTimeout.Should().Be(60);
        ext.ExecutionStrategyFactory.Should().NotBeNull();
        ext.QuerySplittingBehavior.Should().Be(QuerySplittingBehavior.SingleQuery);
    }

    [TestMethod]
    public void Postgres_options_use_npgsql_and_postgres_migrations_assembly()
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            DatabaseProvider.Postgres,
            "Host=localhost;Database=x;",
            new DbProviderTuning(60));

        var ext = builder.Options.FindExtension<NpgsqlOptionsExtension>();
        ext.Should().NotBeNull();
        ext!.MigrationsAssembly.Should().Be("GuideAntsApi.DataModel.Postgres");
        ext.CommandTimeout.Should().Be(60);
        ext.ExecutionStrategyFactory.Should().NotBeNull();
    }
}
