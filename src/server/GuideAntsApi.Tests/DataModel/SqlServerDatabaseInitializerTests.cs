using FluentAssertions;
using GuideAntsApi.Database;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// The SQL Server initializer migrates with SQL Server options whatever <c>Database:Provider</c> the process environment
/// holds. It runs before Program copies configuration into the environment, so it must not read the provider from there
/// (parity report, Limits). The tests set process environment variables, so they don't run in parallel.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SqlServerDatabaseInitializerTests
{
    private const string ConnectionString = "Server=localhost;Initial Catalog=initializer;";

    [TestMethod]
    [DataRow("Database__Provider")]
    [DataRow("Database:Provider")]
    public void Migration_options_are_sql_servers_when_the_environment_says_postgres(string variable)
    {
        var colon = Environment.GetEnvironmentVariable("Database:Provider");
        var underscore = Environment.GetEnvironmentVariable("Database__Provider");
        try
        {
            Environment.SetEnvironmentVariable("Database:Provider", null);
            Environment.SetEnvironmentVariable("Database__Provider", null);
            Environment.SetEnvironmentVariable(variable, "Postgres");
            DatabaseProviderSettings.FromEnvironment().Should().Be(DatabaseProvider.Postgres);

            var options = SqlServerDatabaseInitializer.CreateMigrationOptions(ConnectionString);

            // The options the initializer built before, with the provider unset in the environment.
            var expected = new DbContextOptionsBuilder<ApplicationDbContext>();
            ApplicationDbContextOptions.Configure(
                expected, DatabaseProvider.SqlServer, ConnectionString, new DbProviderTuning(60, true, true));
            var expectedExtensions = expected.Options.Extensions.ToList();
            var actualExtensions = options.Extensions.ToList();
            actualExtensions.Select(e => e.GetType()).Should().Equal(expectedExtensions.Select(e => e.GetType()));
            for (var i = 0; i < expectedExtensions.Count; i++)
            {
                var name = actualExtensions[i].GetType().Name;
                actualExtensions[i].Info.LogFragment.Should().Be(expectedExtensions[i].Info.LogFragment, name);
                actualExtensions[i].Info.ShouldUseSameServiceProvider(expectedExtensions[i].Info).Should().BeTrue(name);
            }

            options.FindExtension<NpgsqlOptionsExtension>().Should().BeNull();
            var sql = options.FindExtension<SqlServerOptionsExtension>()!;
            sql.Should().NotBeNull();
            sql.ConnectionString.Should().Be(ConnectionString);
            sql.CommandTimeout.Should().Be(60);
            sql.ExecutionStrategyFactory.Should().NotBeNull();
            sql.QuerySplittingBehavior.Should().Be(QuerySplittingBehavior.SingleQuery);
            using var context = new ApplicationDbContext(options);
            context.Database.IsSqlServer().Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database:Provider", colon);
            Environment.SetEnvironmentVariable("Database__Provider", underscore);
        }
    }
}
