using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

// Npgsql's query SQL generator and its singleton options are public types in Npgsql's Internal namespaces. Npgsql
// 9.0.4 has no public option for this: its own ReverseNullOrdering switch is internal (it exists for Npgsql's tests).
#pragma warning disable EF1001

namespace GuideAntsApi.DataModel.Database;

/// <summary>
/// Postgres only: makes every <c>ORDER BY</c> sort NULLs the way SQL Server does. SQL Server treats NULL as the lowest
/// value (first when ascending, last when descending); Postgres treats it as the highest. Registered by
/// <see cref="ApplicationDbContextOptions.Configure"/> in place of Npgsql's <c>IQuerySqlGeneratorFactory</c>.
/// </summary>
/// <remarks>
/// Every Npgsql translation and SQL shape is kept: the generator derives from <see cref="NpgsqlQuerySqlGenerator"/>
/// and only appends to each ordering it writes. That covers the query's <c>ORDER BY</c>, window functions
/// (<c>ROW_NUMBER() OVER(... ORDER BY ...)</c>) and ordered aggregates (<c>string_agg(... ORDER BY ...)</c>).
/// </remarks>
public sealed class SqlServerNullOrderingQuerySqlGeneratorFactory(
    QuerySqlGeneratorDependencies dependencies,
    IRelationalTypeMappingSource typeMappingSource,
    INpgsqlSingletonOptions npgsqlSingletonOptions) : IQuerySqlGeneratorFactory
{
    public QuerySqlGenerator Create()
        => new SqlServerNullOrderingQuerySqlGenerator(dependencies, typeMappingSource, npgsqlSingletonOptions.PostgresVersion);
}

/// <summary>
/// Appends <c>NULLS FIRST</c> to an ascending ordering and <c>NULLS LAST</c> to a descending one, except where the
/// sort key can't be NULL. That exception matters for speed, not results: Postgres 18 doesn't use a NOT NULL
/// constraint to drop a NULLS clause, so <c>ORDER BY "Created" DESC NULLS LAST</c> can't be read from a plain index
/// on <c>"Created"</c> (measured: an index scan becomes a sequential scan plus a sort).
/// </summary>
internal sealed class SqlServerNullOrderingQuerySqlGenerator(
    QuerySqlGeneratorDependencies dependencies,
    IRelationalTypeMappingSource typeMappingSource,
    Version postgresVersion)
    // false: Npgsql's own switch would append a second NULLS clause.
    : NpgsqlQuerySqlGenerator(dependencies, typeMappingSource, reverseNullOrderingEnabled: false, postgresVersion)
{
    protected override Expression VisitOrdering(OrderingExpression ordering)
    {
        var result = base.VisitOrdering(ordering);

        if (CanSortNulls(ordering.Expression))
        {
            Sql.Append(ordering.IsAscending ? " NULLS FIRST" : " NULLS LAST");
        }

        return result;
    }

    /// <summary>
    /// False only when no row can have a NULL key: a column EF knows is not nullable (EF marks a column read through
    /// a LEFT JOIN nullable even when the table declares it NOT NULL), or a constant or parameter, which EF writes as
    /// <c>(SELECT 1)</c> and which sorts nothing. Any other expression may be NULL.
    /// </summary>
    private static bool CanSortNulls(SqlExpression key)
        => key is not (ColumnExpression { IsNullable: false } or SqlConstantExpression or SqlParameterExpression);
}
