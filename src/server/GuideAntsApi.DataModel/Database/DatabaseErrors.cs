using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.DataModel.Database;

/// <summary>
/// Reads provider errors the same way on SQL Server and Postgres.
/// </summary>
/// <remarks>
/// The provider exception is either the exception itself or, for a <see cref="DbUpdateException"/> (an error from
/// <c>SaveChanges</c>), its inner exception, the same unwrapping EF Core's execution strategy does. Other wrappers are
/// not looked into. On Postgres a deferred foreign key check (parity report #25) raises its violation from the commit,
/// so that <see cref="PostgresException"/> arrives bare rather than inside a <see cref="DbUpdateException"/>.
/// </remarks>
public static class DatabaseErrors
{
    private const int SqlServerForeignKeyViolation = 547;

    /// <summary>
    /// True when <paramref name="ex"/> is a violation of the foreign key <paramref name="constraintName"/>.
    /// SQL Server: error 547 whose message contains the name. Postgres: SQLSTATE 23503 whose
    /// <see cref="PostgresException.ConstraintName"/> is the name.
    /// </summary>
    public static bool IsForeignKeyViolation(Exception ex, string constraintName)
    {
        ArgumentNullException.ThrowIfNull(ex);
        ArgumentException.ThrowIfNullOrEmpty(constraintName);

        return ProviderException(ex) switch
        {
            SqlException sql => sql.Number == SqlServerForeignKeyViolation
                && sql.Message.Contains(constraintName, StringComparison.Ordinal),
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation
                && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>
    /// True for a Postgres serialization failure (SQLSTATE 40001). A serializable transaction on Postgres reads a
    /// snapshot, so where SQL Server's range locks make a conflicting transaction wait, Postgres aborts one of the two.
    /// SQL Server never reports one, so this is always false there.
    /// </summary>
    public static bool IsSerializationFailure(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ProviderException(ex) is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };
    }

    /// <summary>
    /// Reads the provider error code of <paramref name="ex"/>. SQL Server: <paramref name="code"/> is
    /// <see cref="SqlException.Number"/> and <paramref name="state"/> is <see cref="SqlException.State"/>. Postgres:
    /// <paramref name="code"/> is the SQLSTATE and <paramref name="state"/> is the constraint name (empty when there is
    /// none, and both are empty for a client-side <see cref="NpgsqlException"/> such as a broken connection).
    /// Returns false, with both empty, when the exception doesn't come from either provider.
    /// </summary>
    public static bool TryGetProviderError(Exception ex, out string code, out string state)
    {
        ArgumentNullException.ThrowIfNull(ex);

        switch (ProviderException(ex))
        {
            case SqlException sql:
                code = sql.Number.ToString(CultureInfo.InvariantCulture);
                state = sql.State.ToString(CultureInfo.InvariantCulture);
                return true;
            case PostgresException postgres:
                code = postgres.SqlState;
                state = postgres.ConstraintName ?? string.Empty;
                return true;
            case NpgsqlException npgsql:
                code = npgsql.SqlState ?? string.Empty;
                state = string.Empty;
                return true;
            default:
                code = string.Empty;
                state = string.Empty;
                return false;
        }
    }

    private static Exception ProviderException(Exception ex)
        => ex is DbUpdateException { InnerException: { } inner } ? inner : ex;
}
