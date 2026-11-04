using System.Text.RegularExpressions;
using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// How the database compares and sorts text. SQL Server's default collation ignores case (and keeps accents); on
/// Postgres every string column uses the nondeterministic ICU collation
/// <see cref="PostgresModelAdapter.CaseInsensitiveCollation"/> (<c>und-u-ks-level2</c>), so equality, unique
/// indexes, <c>LIKE</c>, <c>strpos</c> and <c>left</c>/<c>right</c> ignore case the same way. The tests run on both
/// providers against the suite's database (no web host), on <c>Users</c> and <c>Projects</c> rows each test creates.
/// </summary>
/// <remarks>
/// Npgsql 9.0.4 translates <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c> to <c>LIKE</c> for a constant or
/// parameter argument, and to <c>strpos(...) &gt; 0</c> / <c>left</c>/<c>right(...) = ...</c> for a column or
/// computed argument; <c>IndexOf</c> is always <c>strpos</c>. Postgres 18 is the first version that accepts
/// <c>LIKE</c> and <c>strpos</c> on a nondeterministic collation (17 rejects both). <c>ILIKE</c> and regular
/// expressions are still rejected on 18; SQL Server's provider can't translate them, so no shared query uses them.
/// Trailing spaces and the sort order of punctuation are accepted differences (parity report).
/// </remarks>
[TestClass]
public sealed class TextComparisonBehaviorTests
{
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private string _token = null!;
    private readonly List<Guid> _userIds = [];
    private readonly List<Guid> _projectIds = [];

    private static bool OnPostgres => TestContainerManager.Instance.Provider == DatabaseProvider.Postgres;

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
        // Keeps each test's values unique in the shared database. It is lower-case hex, so ToUpperInvariant gives
        // a case variant of it.
        _token = Guid.NewGuid().ToString("N")[..12];
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await using var db = CreateContext();
        await db.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    [TestMethod]
    public async Task Equality_ignores_case()
    {
        await SeedUserAsync($"Ann.{_token}@Example.com");
        var lowerCase = $"ann.{_token}@example.com";

        await using var db = CreateContext();
        (await db.Users.AnyAsync(u => u.Email == lowerCase)).Should().BeTrue();
    }

    [TestMethod]
    public async Task Unique_index_ignores_case()
    {
        await SeedUserAsync($"Ann.{_token}@Example.com");

        var save = () => SeedUserAsync($"ANN.{_token.ToUpperInvariant()}@example.com");

        (await save.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException!.Message.Should().Contain("IX_Users_Email_Unique");
    }

    [TestMethod]
    public async Task Contains_and_StartsWith_ignore_case()
    {
        var ids = new[] { await SeedProjectAsync("Alpha Project"), await SeedProjectAsync("Beta") };

        await using var db = CreateContext();
        var projects = db.Projects.Where(p => ids.Contains(p.Id));
        (await projects.Where(p => p.Title.Contains("ALPHA")).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha Project");
        (await projects.Where(p => p.Title.StartsWith("alpha")).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha Project");
        (await projects.Where(p => EF.Functions.Like(p.Title, "%PROJECT")).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha Project");
    }

    /// <summary>A captured variable is a parameter: Npgsql escapes it at run time and sends <c>LIKE @p</c>.</summary>
    [TestMethod]
    public async Task Parameter_arguments_of_Contains_StartsWith_EndsWith_and_IndexOf_ignore_case()
    {
        var ids = new[] { await SeedProjectAsync("Alpha_Project"), await SeedProjectAsync("Alpha-Project") };
        var contains = "HA_PRO";
        var startsWith = "alpha_";
        var endsWith = "_PROJECT";
        var indexOf = "a_p";

        await using var db = CreateContext();
        var projects = db.Projects.Where(p => ids.Contains(p.Id));
        // The '_' in each argument is a literal, not the LIKE wildcard, so "Alpha-Project" never matches.
        (await projects.Where(p => p.Title.Contains(contains)).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha_Project");
        (await projects.Where(p => p.Title.StartsWith(startsWith)).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha_Project");
        (await projects.Where(p => p.Title.EndsWith(endsWith)).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha_Project");
        (await projects.Where(p => p.Title.IndexOf(indexOf) == 4).Select(p => p.Title).ToListAsync()).Should().Equal("Alpha_Project");
    }

    /// <summary>
    /// Review Focus #1: a column argument can't be escaped into a <c>LIKE</c> pattern, so Npgsql sends
    /// <c>strpos("Title", "Slug") &gt; 0</c>.
    /// </summary>
    [TestMethod]
    public async Task Member_argument_Contains_ignores_case()
    {
        var matching = await SeedProjectAsync($"Alpha-{_token} Project", slug: $"ALPHA-{_token.ToUpperInvariant()}");
        var other = await SeedProjectAsync($"Beta-{_token} Project", slug: $"gamma-{_token}");
        var ids = new[] { matching, other };

        await using var db = CreateContext();
        var titles = await db.Projects
            .Where(p => ids.Contains(p.Id) && p.Title.Contains(p.Slug))
            .Select(p => p.Title)
            .ToListAsync();

        titles.Should().Equal($"Alpha-{_token} Project");
        $"Alpha-{_token} Project".Contains($"ALPHA-{_token.ToUpperInvariant()}", StringComparison.Ordinal)
            .Should().BeFalse("the row matches only when case is ignored");
    }

    /// <summary>
    /// The other column-argument forms: <c>left</c>/<c>right(...) = ...</c> for <c>StartsWith</c>/<c>EndsWith</c>
    /// and <c>strpos</c> for <c>IndexOf</c>.
    /// </summary>
    [TestMethod]
    public async Task Member_argument_StartsWith_EndsWith_and_IndexOf_ignore_case()
    {
        var matching = await SeedProjectAsync($"alpha-{_token} project", slug: $"ALPHA-{_token.ToUpperInvariant()}", description: $"Release ALPHA-{_token}");
        var other = await SeedProjectAsync($"beta-{_token} project", slug: $"gamma-{_token}", description: "Release beta");
        var ids = new[] { matching, other };

        await using var db = CreateContext();
        var projects = db.Projects.Where(p => ids.Contains(p.Id));
        (await projects.Where(p => p.Title.StartsWith(p.Slug)).Select(p => p.Id).ToListAsync()).Should().Equal(matching);
        (await projects.Where(p => p.Description.EndsWith(p.Slug)).Select(p => p.Id).ToListAsync()).Should().Equal(matching);
        (await projects.Where(p => p.Description.IndexOf(p.Slug) == 8).Select(p => p.Id).ToListAsync()).Should().Equal(matching);
    }

    /// <summary>Both collations ignore case but not accents (SQL Server <c>_CI_AS</c>, ICU <c>ks-level2</c>).</summary>
    [TestMethod]
    public async Task Equality_keeps_accents()
    {
        var ids = new[] { await SeedProjectAsync("Café"), await SeedProjectAsync("Cafe") };

        await using var db = CreateContext();
        (await db.Projects.Where(p => ids.Contains(p.Id) && p.Title == "CAFÉ").Select(p => p.Title).ToListAsync()).Should().Equal("Café");
    }

    /// <summary>
    /// Accepted difference: SQL Server pads the shorter value with spaces before comparing, Postgres doesn't. The app
    /// trims user input at the boundaries (parity report), so stored and queried values carry no trailing spaces.
    /// </summary>
    [TestMethod]
    public async Task Trailing_space_comparison()
    {
        await SeedUserAsync($"ann.{_token}@example.com");
        var withTrailingSpace = $"ann.{_token}@example.com ";

        await using var db = CreateContext();
        var found = await db.Users.AnyAsync(u => u.Email == withTrailingSpace);

        found.Should().Be(!OnPostgres, "SQL Server ignores trailing spaces in = and Postgres compares them");
    }

    /// <summary>
    /// Both collations sort by letters ignoring case and put "_x" first. Neither one orders a case tie ("a"/"A"): the
    /// order of a tie depends on the plan (seen: SQL Server "a, A, B, b", Postgres "A, a, b, B" for this input), so
    /// only the case-folded order is pinned. Code that needs a stable order adds a tiebreak.
    /// </summary>
    [TestMethod]
    public async Task Ordinal_sort_of_mixed_case_names()
    {
        var ids = await SeedProjectsAsync("b", "A", "a", "B", "_x");

        var sorted = await SortedTitlesAsync(ids);

        sorted.Select(t => t.ToLowerInvariant()).Should().Equal("_x", "a", "a", "b", "b");
        sorted.Should().BeEquivalentTo("b", "A", "a", "B", "_x");
    }

    /// <summary>
    /// Accepted difference: SQL Server's word sort gives a hyphen almost no weight ("-x" sorts with "x", "a-b" after
    /// "ab"), while ICU sorts punctuation before letters and digits.
    /// </summary>
    [TestMethod]
    public async Task Hyphen_sort_order_differs_by_provider()
    {
        var ids = await SeedProjectsAsync("m", "ab", "-x", "a-b");

        var sorted = await SortedTitlesAsync(ids);

        if (OnPostgres)
        {
            sorted.Should().Equal("-x", "a-b", "ab", "m");
        }
        else
        {
            sorted.Should().Equal("ab", "a-b", "m", "-x");
        }
    }

    /// <summary>
    /// Postgres 18 still rejects <c>ILIKE</c> and regular expressions on a nondeterministic collation. Neither
    /// translates on SQL Server (<c>EF.Functions.ILike</c> is Npgsql-only, and SQL Server's provider has no
    /// <c>Regex</c> translation), so this pins a Postgres limit no shared query can reach.
    /// </summary>
    [TestMethod]
    public async Task Postgres_rejects_ILike_and_regular_expressions_on_case_insensitive_columns()
    {
        DatabaseTestProvider.RequirePostgres();
        var ids = new[] { await SeedProjectAsync("Alpha Project") };

        await using var db = CreateContext();
        var projects = db.Projects.Where(p => ids.Contains(p.Id));
        var iLike = () => projects.AnyAsync(p => EF.Functions.ILike(p.Title, "%alpha%"));
        var regex = () => projects.AnyAsync(p => Regex.IsMatch(p.Title, "^Alpha"));

        (await iLike.Should().ThrowAsync<PostgresException>())
            .Which.Message.Should().Be("0A000: nondeterministic collations are not supported for ILIKE");
        (await regex.Should().ThrowAsync<PostgresException>())
            .Which.Message.Should().Be("0A000: nondeterministic collations are not supported for regular expressions");
    }

    private ApplicationDbContext CreateContext() => new(_options);

    private async Task SeedUserAsync(string email)
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Users.Add(new User { Id = userId, Name = "Text comparison user", Email = email });
        await db.SaveChangesAsync();
        _userIds.Add(userId);
    }

    private async Task<Guid> SeedProjectAsync(string title, string? slug = null, string description = "")
    {
        var projectId = Guid.NewGuid();
        _projectIds.Add(projectId);

        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = projectId, Title = title, Slug = slug ?? $"text-{projectId:N}", Description = description });
        await db.SaveChangesAsync();
        return projectId;
    }

    /// <summary>Seeds one project per title, one save each, in the order given.</summary>
    private async Task<Guid[]> SeedProjectsAsync(params string[] titles)
    {
        var ids = new List<Guid>();
        foreach (var title in titles)
        {
            ids.Add(await SeedProjectAsync(title));
        }
        return [.. ids];
    }

    private async Task<List<string>> SortedTitlesAsync(Guid[] ids)
    {
        await using var db = CreateContext();
        return await db.Projects.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Title).Select(p => p.Title).ToListAsync();
    }
}
