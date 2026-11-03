using Microsoft.Data.SqlClient;

namespace Parity.SqlServer;

internal static class SqlServerConnections
{
    /// <summary>Opens a connection, turning "can't connect" and "no such database" into the CLI's exceptions.</summary>
    public static async Task<SqlConnection> OpenAsync(string connectionString, string profile, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw SqlServerErrors.Translate(ex, profile, SqlServerErrors.Host(connectionString)) ?? (Exception)ex;
        }
    }
}
