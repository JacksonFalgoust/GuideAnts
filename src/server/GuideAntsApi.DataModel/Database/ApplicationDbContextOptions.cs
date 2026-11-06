using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace GuideAntsApi.DataModel.Database;

/// <param name="CommandTimeoutSeconds">Command timeout; generous to ride out transient Azure SQL hiccups.</param>
/// <param name="UseVectorSearch">SQL Server only: enable vector search functions.</param>
/// <param name="ExplicitSingleQuery">
/// Use single-query by default. SplitQuery was causing system-wide lock contention: each split
/// generates uncorrelated scans that block on concurrent write operations (e.g., retention cleanup).
/// Queries that genuinely need split behavior can opt in with .AsSplitQuery() on a case-by-case basis.
/// </param>
public sealed record DbProviderTuning(int CommandTimeoutSeconds = 60, bool UseVectorSearch = false, bool ExplicitSingleQuery = false);

public static class ApplicationDbContextOptions
{
    public const string PostgresMigrationsAssembly = "GuideAntsApi.DataModel.Postgres";

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder builder,
        DatabaseProvider provider,
        string connectionString,
        DbProviderTuning tuning)
    {
        switch (provider)
        {
            case DatabaseProvider.SqlServer:
                builder.UseSqlServer(connectionString, sql =>
                {
                    if (tuning.ExplicitSingleQuery)
                    {
                        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                    }
                    // Enable vector search functions
                    if (tuning.UseVectorSearch)
                    {
                        sql.UseVectorSearch();
                    }
                    // Resilient SQL execution and generous timeout for transient Azure SQL hiccups
                    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                    sql.CommandTimeout(tuning.CommandTimeoutSeconds);
                });
                break;
            case DatabaseProvider.Postgres:
                builder.UseNpgsql(connectionString, npg =>
                {
                    npg.UseVector();
                    // Resilient execution and generous timeout for transient hiccups
                    npg.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                    npg.CommandTimeout(tuning.CommandTimeoutSeconds);
                    npg.MigrationsAssembly(PostgresMigrationsAssembly);
                    if (tuning.ExplicitSingleQuery)
                    {
                        npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                    }
                });
                // Sort NULLs as SQL Server does: first when ascending, last when descending.
                builder.ReplaceService<IQuerySqlGeneratorFactory, SqlServerNullOrderingQuerySqlGeneratorFactory>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }

        return builder;
    }
}
