using Microsoft.Data.SqlClient;
using Parity.Core;

namespace Parity.SqlServer;

public static class SqlServerErrors
{
    // Network, timeout, login and server-unavailable error numbers.
    private static readonly HashSet<int> Unreachable = [2, 40, 53, 121, 233, 1225, 10053, 10054, 10060, 10061, 18456];

    public static string Host(string connectionString) => new SqlConnectionStringBuilder(connectionString).DataSource;

    /// <summary>
    /// The exception to throw for infrastructure failures, or null when this is a query error to report.
    /// A timeout (-2) means "unreachable" only while connecting; during a query it is a slow query.
    /// </summary>
    public static ParityException? Translate(SqlException ex, string profile, string host, bool connecting = true) =>
        Translate(ex.Number, ex.Class, ex.Message, profile, host, connecting, ex);

    internal static ParityException? Translate(int number, int errorClass, string message, string profile, string host, bool connecting, Exception? inner = null) => number switch
    {
        4060 or 208 => new NotSeededException($"Profile '{profile}' isn't seeded. Run `seed --profile {profile}`.", inner),
        _ when Unreachable.Contains(number) || errorClass >= 20 || (connecting && number == -2) => new EngineUnavailableException(
            $"Can't reach profile '{profile}' (SqlServer) at {host}: {message} Is `docker compose up -d` running, and has SQL Server finished starting?", inner),
        _ => null,
    };
}
