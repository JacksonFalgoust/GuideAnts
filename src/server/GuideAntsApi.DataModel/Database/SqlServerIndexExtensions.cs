using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuideAntsApi.DataModel.Database;

/// <summary>
/// Disambiguates <c>IncludeProperties</c>, which both the SQL Server and Npgsql EF providers
/// define with the same signature. Keeps the SQL Server covering-index model unchanged.
/// </summary>
internal static class SqlServerIndexExtensions
{
    public static IndexBuilder<TEntity> SqlServerIncludeProperties<TEntity>(
        this IndexBuilder<TEntity> indexBuilder,
        Expression<Func<TEntity, object?>> includeExpression)
        => SqlServerIndexBuilderExtensions.IncludeProperties(indexBuilder, includeExpression);
}
