using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GuideAntsApi.Database;

/// <summary>
/// An exclusive application lock owned by the current transaction: it is released when the transaction commits or
/// rolls back. Callers start the transaction first.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server: <c>sp_getapplock</c> with <c>@LockOwner = 'Transaction'</c>, which waits up to the timeout. On
/// timeout it returns -1, the return value is ignored, and the transaction carries on without the lock.
/// </para>
/// <para>
/// Postgres: <c>pg_try_advisory_xact_lock</c> on a 64-bit hash of the resource name, tried every
/// <see cref="PostgresPollInterval"/> until it is acquired or the timeout passes. On timeout it logs a warning and,
/// like SQL Server, carries on without the lock. Polling the non-blocking form keeps the timeout in this process,
/// so no <c>lock_timeout</c> setting (and no error that would abort the transaction) is needed.
/// </para>
/// <para>
/// In a serializable Postgres transaction the first statement, here the lock attempt, fixes the snapshot. A caller
/// that had to wait therefore reads data from before the previous holder committed, and Postgres reports any
/// conflicting write with a serialization failure (40001). Serializable callers retry on it (see
/// <c>DatabaseErrors.IsSerializationFailure</c>).
/// </para>
/// </remarks>
public static class DatabaseAppLock
{
    /// <summary>Scalar query of one boolean, aliased <c>Value</c> as EF Core's <c>SqlQuery</c> expects.</summary>
    internal const string PostgresTryLockSql = "SELECT pg_try_advisory_xact_lock(hashtextextended({0}, 0)) AS \"Value\"";

    internal static readonly TimeSpan PostgresPollInterval = TimeSpan.FromMilliseconds(100);

    /// <param name="database">The database of a context with an open transaction.</param>
    /// <param name="resource">A constant lock name (it is inlined into the SQL Server statement, quotes escaped).</param>
    /// <param name="timeout">How long to wait before carrying on without the lock.</param>
    /// <param name="ct">Cancels the wait.</param>
    public static async Task AcquireTransactionLockAsync(
        DatabaseFacade database,
        string resource,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrEmpty(resource);

        if (database.IsNpgsql())
        {
            await AcquirePostgresAsync(database, resource, timeout, ct).ConfigureAwait(false);
            return;
        }

        await database.ExecuteSqlRawAsync(SqlServerLockSql(resource, timeout), ct).ConfigureAwait(false);
    }

    /// <summary>The SQL Server statement; for the existing callers it is exactly the text they sent before.</summary>
    internal static string SqlServerLockSql(string resource, TimeSpan timeout)
    {
        var milliseconds = ((long)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        return $"EXEC sp_getapplock @Resource = N'{resource.Replace("'", "''", StringComparison.Ordinal)}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {milliseconds};";
    }

    private static async Task AcquirePostgresAsync(
        DatabaseFacade database,
        string resource,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var acquired = await database
                .SqlQueryRaw<bool>(PostgresTryLockSql, resource)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (acquired.Single())
            {
                return;
            }

            var remaining = timeout - waited.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                database.GetService<ILoggerFactory>()
                    .CreateLogger(typeof(DatabaseAppLock).FullName!)
                    .LogWarning(
                        "Timed out after {TimeoutMs} ms waiting for application lock {Resource}; continuing without it, as sp_getapplock does on SQL Server.",
                        (long)timeout.TotalMilliseconds,
                        resource);
                return;
            }

            await Task.Delay(remaining < PostgresPollInterval ? remaining : PostgresPollInterval, ct).ConfigureAwait(false);
        }
    }
}
