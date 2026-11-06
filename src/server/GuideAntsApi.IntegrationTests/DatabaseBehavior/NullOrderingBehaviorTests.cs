using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Where the database sorts NULLs. SQL Server treats NULL as the lowest value: first in an ascending order, last in a
/// descending one. Postgres treats NULL as the highest value, so on Postgres the query SQL generator appends
/// <c>NULLS FIRST</c> to an ascending ordering and <c>NULLS LAST</c> to a descending one
/// (<c>SqlServerNullOrderingQuerySqlGeneratorFactory</c>). The tests run on both providers against the suite's
/// database (no web host), on <c>Users</c> rows each test creates and expect the SQL Server order.
/// </summary>
/// <remarks>
/// <c>Users</c> is a root table (only <c>Name</c> and <c>Email</c> are required) with unindexed nullable columns of
/// both kinds the brief asks for: <c>Locale</c> (string) and <c>LastLoginAt</c> (DateTime). Its optional
/// self-reference <c>ApprovedByUser</c> gives a LEFT JOIN, so the outer-join case needs no other table.
/// </remarks>
[TestClass]
public sealed class NullOrderingBehaviorTests
{
    private static readonly DateTime Earlier = new(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 2, 20, 9, 30, 0, DateTimeKind.Utc);

    private DbContextOptions<ApplicationDbContext> _options = null!;
    private string _token = null!;
    private readonly List<Guid> _userIds = [];

    [TestInitialize]
    public async Task TestInitialize()
    {
        var connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            TestContainerManager.Instance.Provider,
            connectionString,
            new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));
        _options = builder.Options;
        _token = Guid.NewGuid().ToString("N")[..12];
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        // Approved users first, so the self-referencing foreign key never blocks the cleanup.
        await using var db = CreateContext();
        await db.Users.Where(u => _userIds.Contains(u.Id) && u.ApprovedByUserId != null).ExecuteDeleteAsync();
        await db.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }

    [TestMethod]
    public async Task Ascending_puts_nulls_first()
    {
        var (none, a, b) = await SeedNullAandBAsync();

        await using var db = CreateContext();
        var users = db.Users.Where(u => _userIds.Contains(u.Id));
        (await users.OrderBy(u => u.Locale).Select(u => u.Locale).ToListAsync()).Should().Equal(null, "a", "b");
        (await users.OrderBy(u => u.LastLoginAt).Select(u => u.Id).ToListAsync()).Should().Equal(none, a, b);
    }

    [TestMethod]
    public async Task Descending_puts_nulls_last()
    {
        var (none, a, b) = await SeedNullAandBAsync();

        await using var db = CreateContext();
        var users = db.Users.Where(u => _userIds.Contains(u.Id));
        (await users.OrderByDescending(u => u.Locale).Select(u => u.Locale).ToListAsync()).Should().Equal("b", "a", null);
        (await users.OrderByDescending(u => u.LastLoginAt).Select(u => u.Id).ToListAsync()).Should().Equal(b, a, none);
    }

    /// <summary>All three rows tie on the (non-nullable) first key, so the nullable second key decides.</summary>
    [TestMethod]
    public async Task ThenBy_puts_nulls_first_and_ThenByDescending_puts_them_last()
    {
        var (none, a, b) = await SeedNullAandBAsync();

        await using var db = CreateContext();
        var users = db.Users.Where(u => _userIds.Contains(u.Id));
        (await users.OrderBy(u => u.Name).ThenBy(u => u.Locale).Select(u => u.Locale).ToListAsync())
            .Should().Equal(null, "a", "b");
        (await users.OrderBy(u => u.Name).ThenByDescending(u => u.Locale).Select(u => u.Locale).ToListAsync())
            .Should().Equal("b", "a", null);
        (await users.OrderBy(u => u.Name).ThenBy(u => u.LastLoginAt).Select(u => u.Id).ToListAsync())
            .Should().Equal(none, a, b);
        (await users.OrderBy(u => u.Name).ThenByDescending(u => u.LastLoginAt).Select(u => u.Id).ToListAsync())
            .Should().Equal(b, a, none);
    }

    /// <summary><c>Take(1)</c> of an ascending order is the NULL row, as on SQL Server ("oldest login first").</summary>
    [TestMethod]
    public async Task First_of_an_ascending_order_is_the_null_row()
    {
        var (none, _, b) = await SeedNullAandBAsync();

        await using var db = CreateContext();
        var users = db.Users.Where(u => _userIds.Contains(u.Id));
        (await users.OrderBy(u => u.LastLoginAt).Select(u => u.Id).FirstAsync()).Should().Be(none);
        (await users.OrderByDescending(u => u.LastLoginAt).Select(u => u.Id).FirstAsync()).Should().Be(b);
    }

    /// <summary>A computed key (<c>COALESCE</c>) is not a column, so its nullability is unknown; NULLs still go first.</summary>
    [TestMethod]
    public async Task Computed_ordering_key_puts_nulls_first()
    {
        await SeedUserAsync(locale: null, timeZone: null);
        await SeedUserAsync(locale: null, timeZone: "b");
        await SeedUserAsync(locale: "a", timeZone: null);

        await using var db = CreateContext();
        var users = db.Users.Where(u => _userIds.Contains(u.Id));
        (await users.OrderBy(u => u.Locale ?? u.TimeZone).Select(u => u.Locale ?? u.TimeZone).ToListAsync())
            .Should().Equal(null, "a", "b");
        (await users.OrderByDescending(u => u.Locale ?? u.TimeZone).Select(u => u.Locale ?? u.TimeZone).ToListAsync())
            .Should().Equal("b", "a", null);
    }

    /// <summary>
    /// <c>Users.Created</c> is NOT NULL, but read through the optional <c>ApprovedByUser</c> navigation it comes from a
    /// LEFT JOIN, so it is NULL for a user nobody approved. EF marks such a column nullable, and it sorts like SQL
    /// Server.
    /// </summary>
    [TestMethod]
    public async Task Ordering_by_a_required_column_of_a_left_join_puts_missing_rows_first()
    {
        var earlierApprover = await SeedUserAsync(created: Earlier);
        var laterApprover = await SeedUserAsync(created: Later);
        var approvedEarlier = await SeedUserAsync(approvedBy: earlierApprover);
        var approvedLater = await SeedUserAsync(approvedBy: laterApprover);
        var notApproved = await SeedUserAsync();
        var ids = new[] { approvedEarlier, approvedLater, notApproved };

        await using var db = CreateContext();
        var users = db.Users.Where(u => ids.Contains(u.Id));
        (await users.OrderBy(u => u.ApprovedByUser!.Created).Select(u => u.Id).ToListAsync())
            .Should().Equal(notApproved, approvedEarlier, approvedLater);
        (await users.OrderByDescending(u => u.ApprovedByUser!.Created).Select(u => u.Id).ToListAsync())
            .Should().Equal(approvedLater, approvedEarlier, notApproved);
    }

    private ApplicationDbContext CreateContext() => new(_options);

    /// <summary>
    /// Seeds three users that share one <c>Name</c>: Locale/LastLoginAt of NULL/NULL, "a"/<see cref="Earlier"/> and
    /// "b"/<see cref="Later"/>, inserted in the order NULL, "b", "a" so that insertion order can't pass the tests.
    /// </summary>
    private async Task<(Guid None, Guid A, Guid B)> SeedNullAandBAsync()
    {
        var none = await SeedUserAsync(locale: null, lastLoginAt: null);
        var b = await SeedUserAsync(locale: "b", lastLoginAt: Later);
        var a = await SeedUserAsync(locale: "a", lastLoginAt: Earlier);
        return (none, a, b);
    }

    private async Task<Guid> SeedUserAsync(
        string? locale = null,
        DateTime? lastLoginAt = null,
        string? timeZone = null,
        Guid? approvedBy = null,
        DateTime? created = null)
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Id = userId,
            Name = $"Null ordering {_token}",
            Email = $"null-order.{userId:N}@example.com",
            Locale = locale,
            LastLoginAt = lastLoginAt,
            TimeZone = timeZone,
            ApprovedByUserId = approvedBy,
            Created = created ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        _userIds.Add(userId);
        return userId;
    }
}
