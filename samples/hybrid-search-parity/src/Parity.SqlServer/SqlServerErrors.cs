using Microsoft.Data.SqlClient;
using Parity.Core;

namespace Parity.SqlServer;

public static class SqlServerErrors
{
    // Network, timeout, login and server-unavailable error numbers.
    private static readonly HashSet<int> Unreachable = [-2, 2, 40, 53, 121, 233, 1225, 10053, 10054, 10060, 10061, 18456];

    public static string Host(string connectionString) => new SqlConnectionStringBuilder(connectionString).DataSource;

    /// <summary>The exception to throw for infrastructure failures, or null when this is a query error to report.</summary>
    public static ParityException? Translate(SqlException ex, string profile, string host) => ex.Number switch
    {
        4060 or 208 => new NotSeededException($"Profile '{profile}' isn't seeded. Run `seed --profile {profile}`.", ex),
        _ when Unreachable.Contains(ex.Number) || ex.Class >= 20 => new EngineUnavailableException(
            $"Can't reach profile '{profile}' (SqlServer) at {host}: {ex.Message} Is `docker compose up -d` running, and has SQL Server finished starting?", ex),
        _ => null,
    };
}
