using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// The <c>ORDER BY</c> SQL each provider sends (<c>ToQueryString</c> needs no database). On Postgres a sort key that
/// can be NULL gets <c>NULLS FIRST</c> when ascending and <c>NULLS LAST</c> when descending, so NULLs sort as on SQL
/// Server; a key that can't be NULL gets no clause, so a plain index can still serve the order. SQL Server's SQL is
/// unchanged. The integration tests in <c>NullOrderingBehaviorTests</c> check the resulting row order on both providers.
/// </summary>
[TestClass]
public sealed class NullOrderingSqlTests
{
    [TestMethod]
    public void Postgres_appends_nulls_first_to_ascending_and_nulls_last_to_descending_nullable_keys()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        db.Users.OrderBy(u => u.Locale).ToQueryString().Should().Contain("ORDER BY u.\"Locale\" NULLS FIRST");
        db.Users.OrderByDescending(u => u.LastLoginAt).ToQueryString().Should().Contain("ORDER BY u.\"LastLoginAt\" DESC NULLS LAST");
    }

    [TestMethod]
    public void Postgres_ThenBy_keys_each_get_their_own_clause()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        var sql = db.Users.OrderBy(u => u.Name).ThenBy(u => u.Locale).ThenByDescending(u => u.LastLoginAt).ToQueryString();

        sql.Should().Contain("ORDER BY u.\"Name\", u.\"Locale\" NULLS FIRST, u.\"LastLoginAt\" DESC NULLS LAST");
    }

    [TestMethod]
    public void Postgres_leaves_keys_that_cannot_be_null_without_a_clause()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        db.Users.OrderBy(u => u.Created).ToQueryString().Should().Contain("ORDER BY u.\"Created\"").And.NotContain("NULLS");
        db.Users.OrderByDescending(u => u.Created).ThenBy(u => u.Id).ToQueryString()
            .Should().Contain("ORDER BY u.\"Created\" DESC, u.\"Id\"").And.NotContain("NULLS");
    }

    [TestMethod]
    public void Postgres_treats_a_required_column_read_through_a_left_join_as_nullable()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        var sql = db.Users.OrderBy(u => u.ApprovedByUser!.Created).ToQueryString();

        sql.Should().Contain("LEFT JOIN").And.Contain("ORDER BY u0.\"Created\" NULLS FIRST");
    }

    [TestMethod]
    public void Postgres_appends_the_clause_to_a_computed_key()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        db.Users.OrderByDescending(u => u.Locale ?? u.TimeZone).ToQueryString()
            .Should().Contain("ORDER BY COALESCE(u.\"Locale\", u.\"TimeZone\") DESC NULLS LAST");
    }

    [TestMethod]
    public void Postgres_appends_the_clause_inside_a_window_function()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        var sql = db.Users.GroupBy(u => u.Name).Select(g => g.OrderBy(u => u.LastLoginAt).First()).ToQueryString();

        sql.Should().Contain("ROW_NUMBER() OVER(PARTITION BY u0.\"Name\" ORDER BY u0.\"LastLoginAt\" NULLS FIRST)");
    }

    [TestMethod]
    public void Postgres_appends_the_clause_inside_an_ordered_aggregate()
    {
        using var db = CreateContext(DatabaseProvider.Postgres);

        var sql = db.Users
            .GroupBy(u => u.Name)
            .Select(g => string.Join(",", g.OrderByDescending(u => u.Locale).Select(u => u.Email)))
            .ToQueryString();

        sql.Should().Contain("ORDER BY u.\"Locale\" DESC NULLS LAST)");
    }

    [TestMethod]
    public void SqlServer_order_by_is_unchanged()
    {
        using var db = CreateContext(DatabaseProvider.SqlServer);

        var sql = db.Users.OrderBy(u => u.Locale).ThenByDescending(u => u.LastLoginAt).ToQueryString();

        sql.Should().Contain("ORDER BY [u].[Locale], [u].[LastLoginAt] DESC").And.NotContain("NULLS");
    }

    private static ApplicationDbContext CreateContext(DatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            provider,
            provider == DatabaseProvider.Postgres ? "Host=localhost;Database=model_only" : "Server=localhost;Database=model_only;",
            new DbProviderTuning());
        return new ApplicationDbContext(builder.Options);
    }
}
