using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.DataModel.Database;

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
                    if (tuning.UseVectorSearch)
                    {
                        sql.UseVectorSearch();
                    }
                    sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                    sql.CommandTimeout(tuning.CommandTimeoutSeconds);
                });
                break;
            case DatabaseProvider.Postgres:
                builder.UseNpgsql(connectionString, npg =>
                {
                    npg.UseVector();
                    npg.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                    npg.CommandTimeout(tuning.CommandTimeoutSeconds);
                    npg.MigrationsAssembly(PostgresMigrationsAssembly);
                    if (tuning.ExplicitSingleQuery)
                    {
                        npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                    }
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }

        return builder;
    }
}
