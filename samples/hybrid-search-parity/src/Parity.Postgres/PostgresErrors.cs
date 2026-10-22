using System.Net.Sockets;
using Npgsql;
using Parity.Core;
using Parity.Core.Model;
using Pgvector.Npgsql;

namespace Parity.Postgres;

public static class PostgresErrors
{
    public static string Host(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}";
    }

    /// <summary>
    /// The exception to throw for infrastructure failures, or null when this is a query error to report.
    /// A timeout means "unreachable" only while connecting; during a query it is a slow query.
    /// </summary>
    public static ParityException? Translate(Exception ex, string profile, string host, bool connecting = true) => ex switch
    {
        PostgresException { SqlState: "3D000" or "42P01" } =>
            new NotSeededException($"Profile '{profile}' isn't seeded. Run `seed --profile {profile}`.", ex),
        PostgresException pg when pg.SqlState.StartsWith("08", StringComparison.Ordinal) || pg.SqlState is "57P03" or "28P01" =>
            Unreachable(ex, profile, host),
        PostgresException => null,
        NpgsqlException { InnerException: TimeoutException } or TimeoutException when !connecting => null,
        NpgsqlException or SocketException or TimeoutException => Unreachable(ex, profile, host),
        _ => null,
    };

    public static EngineError ToEngineError(PostgresException ex) => new($"PG{ex.SqlState}", ex.MessageText);

    public static EngineError ToEngineError(Exception ex) =>
        ex is PostgresException pg ? ToEngineError(pg) : new EngineError("PGTIMEOUT", ex.Message);

    private static EngineUnavailableException Unreachable(Exception ex, string profile, string host) =>
        new($"Can't reach profile '{profile}' (Postgres) at {host}: {ex.Message} Is `docker compose up -d` running?", ex);
}

public static class PostgresDataSources
{
    /// <summary>A data source with pgvector's type mapping, needed to pass Pgvector.Vector parameters.</summary>
    public static NpgsqlDataSource Build(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector(); // Npgsql.VectorExtensions, from the Pgvector package
        return builder.Build();
    }
}
