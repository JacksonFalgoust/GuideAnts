using FluentAssertions;
using GuideAntsApi.Database;
using GuideAntsApi.DatabaseParity.Tests.Infrastructure;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GuideAntsApi.DatabaseParity.Tests;

[TestClass]
public sealed class PostgresDatabaseInitializerTests
{
    [TestMethod]
    public async Task Creates_a_missing_database_and_applies_the_baseline()
    {
        // Spaces and a double quote prove the name is quoted, not interpolated.
        var database = $"ga init \"quoted\" {Guid.NewGuid():N}"[..40];
        var connectionString = ParityAssembly.Databases.PostgresConnectionStringFor(database);
        var logger = new ListLogger();

        try
        {
            PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, logger);

            (await DatabaseExistsAsync(database)).Should().BeTrue();
            await using var context = ParityDatabases.CreateContext(DatabaseProvider.Postgres, connectionString);
            (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle()
                .Which.Should().EndWith("_PostgresBaselineV1");
            logger.Messages.Should().Equal(
                $"Created Postgres database '{database}'.",
                $"Postgres database '{database}' is ready; applying EF Core migrations.");
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [TestMethod]
    public async Task Keeps_an_existing_database()
    {
        var database = $"ga_init_{Guid.NewGuid():N}";
        var connectionString = ParityAssembly.Databases.PostgresConnectionStringFor(database);

        try
        {
            PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, new ListLogger());
            var logger = new ListLogger();

            PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, logger);

            logger.Messages.Should().Equal(
                $"Postgres database '{database}' already exists.",
                $"Postgres database '{database}' is ready; applying EF Core migrations.");
            await using var context = ParityDatabases.CreateContext(DatabaseProvider.Postgres, connectionString);
            (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [TestMethod]
    public void Rejects_a_database_name_longer_than_63_characters()
    {
        var connectionString = ParityAssembly.Databases.PostgresConnectionStringFor(new string('a', 64));

        var act = () => PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, new ListLogger());

        act.Should().Throw<InvalidOperationException>().WithMessage("Database name exceeds maximum length (63).");
    }

    [TestMethod]
    public void Accepts_a_database_name_of_63_characters()
    {
        var database = new string('b', 63);
        var connectionString = ParityAssembly.Databases.PostgresConnectionStringFor(database);

        try
        {
            var act = () => PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, new ListLogger());

            act.Should().NotThrow();
        }
        finally
        {
            DropDatabaseAsync(database).GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void Requires_a_database_name()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ParityAssembly.Databases.PostgresConnectionString)
        {
            Database = null,
        }.ConnectionString;

        var act = () => PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, new ListLogger());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Connection string must specify Database for automatic database setup.");
    }

    private static async Task<bool> DatabaseExistsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(ParityAssembly.Databases.PostgresConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task DropDatabaseAsync(string database)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(ParityAssembly.Databases.PostgresConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{database.Replace("\"", "\"\"")}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
