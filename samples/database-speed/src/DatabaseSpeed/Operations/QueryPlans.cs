using System.Data.Common;
using System.Text;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>An operation whose query plan RESULTS.md shows, because a carry-over item is about which index it can use.</summary>
public interface IExplainable
{
    string ExplainTitle { get; }

    /// <summary>The query, with the same WHERE and ORDER BY as the production call.</summary>
    IQueryable ExplainQuery(ApplicationDbContext db);
}

/// <summary>
/// The actual plan of the SQL EF generates for a query, run once with its parameters: <c>SET STATISTICS PROFILE ON</c>
/// on SQL Server (which, unlike SHOWPLAN_TEXT, also covers the parameterized <c>sp_executesql</c> call), and
/// <c>EXPLAIN (ANALYZE, TIMING OFF, SUMMARY OFF)</c> on Postgres. Read after the measured runs, on the seeded database.
/// </summary>
public static class QueryPlans
{
    public static async Task<string> ExplainAsync(ProviderTarget target, IExplainable query, CancellationToken ct)
    {
        try
        {
            await using var scope = target.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.OpenConnectionAsync(ct);
            await using var command = query.ExplainQuery(db).CreateDbCommand();
            var plan = new StringBuilder();
            if (target.Provider == DatabaseProvider.SqlServer)
            {
                await ExecuteAsync(db, "SET STATISTICS PROFILE ON", ct);
                try
                {
                    await ReadPlanAsync(command, plan, ct);
                }
                finally
                {
                    await ExecuteAsync(db, "SET STATISTICS PROFILE OFF", ct);
                }
            }
            else
            {
                command.CommandText = "EXPLAIN (ANALYZE, TIMING OFF, SUMMARY OFF) " + command.CommandText;
                await ReadPlanAsync(command, plan, ct);
            }

            return plan.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(plan unavailable: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static async Task ExecuteAsync(ApplicationDbContext db, string sql, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Postgres: one text column per plan line. SQL Server: the query's own rows come first and are skipped; the
    /// STATISTICS PROFILE result set (columns <c>Rows</c>, <c>Executes</c>, <c>StmtText</c>, ...) follows.
    /// </summary>
    private static async Task ReadPlanAsync(DbCommand command, StringBuilder plan, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        do
        {
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var profile = columns.Contains("StmtText") && columns.Contains("Rows");
            var postgres = columns is ["QUERY PLAN"];
            while (await reader.ReadAsync(ct))
            {
                if (profile)
                {
                    plan.Append(Convert.ToString(reader["Rows"], System.Globalization.CultureInfo.InvariantCulture)?.PadLeft(7))
                        .Append(" rows | ")
                        .AppendLine(reader["StmtText"]?.ToString()?.TrimEnd());
                }
                else if (postgres)
                {
                    plan.AppendLine(reader.GetString(0).TrimEnd());
                }
            }
        }
        while (await reader.NextResultAsync(ct));
    }
}
