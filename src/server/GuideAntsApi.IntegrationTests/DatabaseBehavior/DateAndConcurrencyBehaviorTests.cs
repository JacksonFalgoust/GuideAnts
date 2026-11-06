using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Models.Settings;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Dates, NUL characters, string lengths and row versions, on both providers. The tests expect what SQL Server does,
/// except where a difference is accepted (parity report), in which case they pin each provider's result.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>SQL Server's <c>datetime2(7)</c> keeps 100 ns ticks; Postgres <c>timestamptz</c> keeps whole microseconds. On
/// Postgres every DateTime is truncated to whole microseconds before it is written (tracked values too), so a saved
/// value reads back equal.</item>
/// <item>SQL Server compares a DateTime parameter's raw ticks whatever its <c>Kind</c>. Npgsql refuses to send a
/// non-UTC DateTime to <c>timestamptz</c>, so on Postgres every DateTime is marked UTC (never converted) on the way
/// in.</item>
/// <item>Postgres <c>text</c> can't hold U+0000; on Postgres it is stored as U+FFFD (accepted difference).</item>
/// <item>A string longer than its column fails on both providers; nothing truncates it.</item>
/// <item>SQL Server <c>rowversion</c> becomes the Postgres <c>xmin</c> system column: 4 bytes instead of 8, and it
/// changes once per transaction or savepoint rather than once per statement. EF puts every <c>SaveChanges</c> inside a
/// user transaction in its own savepoint (unless auto-savepoints are off), so tracked saves still get a new token every
/// time; only set-based updates without a savepoint between them share one.</item>
/// <item>Column defaults (<c>GETUTCDATE()</c> on SQL Server, <c>statement_timestamp()</c> on Postgres) are evaluated
/// per statement on both, not once per transaction.</item>
/// </list>
/// The DB tests use their own contexts on the suite's database; only <see cref="Settings_endpoint_rejects_stale_row_version"/>
/// goes through the web host.
/// </remarks>
[TestClass]
public sealed class DateAndConcurrencyBehaviorTests : BaseIntegrationTest
{
    /// <summary>7 fractional digits: the last one is below Postgres' microsecond.</summary>
    private static readonly DateTime SevenDigitInstant =
        new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);

    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly List<Guid> _userIds = [];
    private readonly List<Guid> _projectIds = [];
    private readonly List<string> _sectionNames = [];

    private static bool IsPostgres => TestContainerManager.Instance.Provider == DatabaseProvider.Postgres;

    [ClassInitialize]
    public static Task ClassInitialize(TestContext context) => InitializeSharedFactoryAsync(context);

    [ClassCleanup]
    public static Task ClassCleanup() => DisposeSharedFactoryAsync();

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
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await using var db = CreateContext();
        await db.NotebookConversationMessages
            .Where(m => _projectIds.Contains(m.NotebookConversation.Notebook.ProjectId))
            .ExecuteDeleteAsync();
        await db.Notebooks.Where(n => _projectIds.Contains(n.ProjectId)).ExecuteDeleteAsync();
        await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await db.ApplicationSettings.Where(s => _sectionNames.Contains(s.SectionName)).ExecuteDeleteAsync();
    }

    // ------------------------------------------------------------------
    // Dates
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task DateTime_round_trips_exactly()
    {
        var user = NewUser();
        user.Created = SevenDigitInstant;
        user.LastLoginAt = SevenDigitInstant;

        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // Postgres keeps whole microseconds, so the tracked value is truncated to what is stored; SQL Server keeps it.
        var expected = IsPostgres ? SevenDigitInstant.AddTicks(-7) : SevenDigitInstant;
        user.Created.Should().Be(expected);
        user.Created.Ticks.Should().Be(expected.Ticks);
        user.LastLoginAt.Should().Be(expected);

        await using var read = CreateContext();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.Created.Ticks.Should().Be(user.Created.Ticks);
        stored.LastLoginAt!.Value.Ticks.Should().Be(user.LastLoginAt!.Value.Ticks);

        // A parameter with the original 7 digits finds the row, and a microsecond later doesn't.
        var instant = SevenDigitInstant;
        (await read.Users.CountAsync(u => u.Id == user.Id && u.Created == instant && u.LastLoginAt == instant)).Should().Be(1);
        var later = SevenDigitInstant.AddTicks(10);
        (await read.Users.CountAsync(u => u.Id == user.Id && u.Created == later)).Should().Be(0);

        // timestamptz reads back as UTC; datetime2 has no offset, so SQL Server reads Unspecified (accepted difference;
        // the app treats Unspecified as UTC everywhere).
        var expectedKind = IsPostgres ? DateTimeKind.Utc : DateTimeKind.Unspecified;
        stored.Created.Kind.Should().Be(expectedKind);
        stored.LastLoginAt!.Value.Kind.Should().Be(expectedKind);
    }

    [TestMethod]
    public async Task DateTime_round_trips_exactly_after_an_update()
    {
        var user = NewUser();
        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            tracked.LastLoginAt = SevenDigitInstant;
            await db.SaveChangesAsync();

            await using var read = CreateContext();
            var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            stored.LastLoginAt!.Value.Ticks.Should().Be(tracked.LastLoginAt!.Value.Ticks);
        }
    }

    /// <summary>
    /// Postgres counts microseconds from 2000-01-01. Npgsql divides the ticks before that date toward zero (up toward
    /// 2000), so the value is cut down to a whole microsecond before Npgsql sees it, as the tracked value is.
    /// </summary>
    [TestMethod]
    public async Task DateTime_before_2000_round_trips_exactly()
    {
        var beforeEpoch = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999995);
        var user = NewUser();
        user.Created = beforeEpoch;

        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        user.Created.Ticks.Should().Be(beforeEpoch.Ticks - (IsPostgres ? 5 : 0));
        await using var read = CreateContext();
        (await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Created.Ticks.Should().Be(user.Created.Ticks);
        (await read.Users.CountAsync(u => u.Id == user.Id && u.Created == beforeEpoch)).Should().Be(1);
    }

    /// <summary>
    /// <c>DateTime.MaxValue</c> (9999-12-31 23:59:59.9999999) is not a whole microsecond. Npgsql writes it as
    /// <c>infinity</c> and reads that back as <c>MaxValue</c>, so the truncation must leave it alone.
    /// </summary>
    [TestMethod]
    public async Task DateTime_min_and_max_values_round_trip()
    {
        var max = NewUser();
        max.Created = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        max.LastLoginAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        await using (var db = CreateContext())
        {
            db.Users.Add(max);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == max.Id);
        stored.Created.Should().Be(DateTime.MaxValue);
        stored.LastLoginAt.Should().Be(DateTime.MinValue);
        (await read.Users.CountAsync(u => u.Id == max.Id && u.Created == DateTime.MaxValue)).Should().Be(1);

        // Accepted difference: in the database itself Postgres holds infinity / -infinity, SQL Server the real dates, so
        // date arithmetic written in SQL treats them differently.
        var id = max.Id;
        var raw = IsPostgres
            ? await read.Database.SqlQuery<string>(
                $"""SELECT "Created"::text || ' ' || "LastLoginAt"::text AS "Value" FROM "Users" WHERE "Id" = {id}""").SingleAsync()
            : await read.Database.SqlQuery<string>(
                $"SELECT CONVERT(varchar(30), Created, 126) + ' ' + CONVERT(varchar(30), LastLoginAt, 126) AS Value FROM Users WHERE Id = {id}").SingleAsync();
        raw.Should().Be(IsPostgres ? "infinity -infinity" : "9999-12-31T23:59:59.9999999 0001-01-01T00:00:00");
    }

    /// <summary>
    /// <c>GETUTCDATE()</c> is evaluated per statement; so is Postgres' <c>statement_timestamp()</c>, while <c>now()</c>
    /// is the transaction's start time. Two rows inserted by two saves in one transaction, both taking the column
    /// default, get different timestamps on both providers.
    /// </summary>
    [TestMethod]
    public async Task Column_defaults_are_evaluated_per_statement()
    {
        var first = NewUser();
        var second = NewUser();
        first.Created = default;
        second.Created = default;

        await using (var db = CreateContext())
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Users.Add(first);
                await db.SaveChangesAsync();
                await Task.Delay(50);
                db.Users.Add(second);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }

        // The database filled in the defaults, and EF read them back.
        first.Created.Should().NotBe(default);
        second.Created.Should().NotBe(default);
        second.Created.Should().BeAfter(first.Created);

        await using var read = CreateContext();
        var stored = await read.Users.AsNoTracking()
            .Where(u => u.Id == first.Id || u.Id == second.Id)
            .ToDictionaryAsync(u => u.Id, u => u.Created);
        stored[first.Id].Ticks.Should().Be(first.Created.Ticks);
        stored[second.Id].Should().BeAfter(stored[first.Id]);
    }

    /// <summary>Review Focus #4: parameters whose Kind is not UTC compare by their raw ticks, as on SQL Server.</summary>
    /// <remarks>
    /// The Kind tests only tell "raw ticks" from <c>ToUniversalTime</c> on a host whose time zone is not UTC (such as a
    /// developer machine): on a UTC host, CI's Linux runners included, both give the same ticks. There they still prove
    /// that a non-UTC Kind doesn't throw.
    /// </remarks>
    [TestMethod]
    public async Task Unspecified_and_Local_query_parameters_compare_raw_ticks()
    {
        var created = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var user = await SeedUserAsync(created);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var fromLocal = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var sameUnspecified = DateTime.SpecifyKind(created, DateTimeKind.Unspecified);
        var sameLocal = DateTime.SpecifyKind(created, DateTimeKind.Local);
        DateTime? nullableLocal = sameLocal;

        await using var db = CreateContext();
        var users = db.Users.Where(u => u.Id == user);
        (await users.CountAsync(u => u.Created >= from)).Should().Be(1);
        (await users.CountAsync(u => u.Created >= fromLocal)).Should().Be(1);
        // Raw ticks: a Local value with the stored ticks is equal, whatever the machine's time zone (only observable
        // on a non-UTC host; see the remarks).
        (await users.CountAsync(u => u.Created == sameUnspecified)).Should().Be(1);
        (await users.CountAsync(u => u.Created == sameLocal)).Should().Be(1);
        (await users.CountAsync(u => u.LastLoginAt == nullableLocal)).Should().Be(0);
        (await users.CountAsync(u => u.Created > sameLocal)).Should().Be(0);
    }

    /// <summary>
    /// Constants are written into the SQL as literals rather than sent as parameters. As above, raw ticks vs
    /// <c>ToUniversalTime</c> is only observable on a non-UTC host.
    /// </summary>
    [TestMethod]
    public async Task Unspecified_and_Local_query_constants_compare_raw_ticks()
    {
        var user = await SeedUserAsync(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        await using var db = CreateContext();
        var users = db.Users.Where(u => u.Id == user);
        (await users.CountAsync(u => u.Created >= new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified))).Should().Be(1);
        (await users.CountAsync(u => u.Created >= new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local))).Should().Be(1);
        (await users.CountAsync(u => u.Created == new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Local))).Should().Be(1);
    }

    /// <summary>
    /// <c>ExecuteUpdate</c> bypasses the SaveChanges guard (which rejects Local values), so SQL Server stores the raw
    /// ticks of whatever it is given. Postgres stores the same ticks. As above, raw ticks vs <c>ToUniversalTime</c> is
    /// only observable on a non-UTC host.
    /// </summary>
    [TestMethod]
    public async Task Unspecified_and_Local_ExecuteUpdate_values_store_raw_ticks()
    {
        var user = await SeedUserAsync(DateTime.UtcNow);
        var unspecified = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Unspecified).AddTicks(1234567);
        var local = new DateTime(2026, 3, 5, 5, 6, 7, DateTimeKind.Local).AddTicks(1234567);
        // Postgres drops the last (sub-microsecond) digit.
        var droppedTicks = IsPostgres ? 7 : 0;

        await using (var db = CreateContext())
        {
            (await db.Users.Where(u => u.Id == user)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Created, unspecified).SetProperty(u => u.LastLoginAt, local)))
                .Should().Be(1);
        }

        await using var read = CreateContext();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == user);
        stored.Created.Ticks.Should().Be(unspecified.Ticks - droppedTicks);
        stored.LastLoginAt!.Value.Ticks.Should().Be(local.Ticks - droppedTicks);
    }

    /// <summary>
    /// The message ordering guard compares a new message's <c>Created</c> with stored ones. On Postgres the stored
    /// value lost its sub-microsecond digits, so the guard compares at that precision; otherwise a message saved with
    /// the same timestamp as a stored higher-sequence message would be rejected on Postgres only.
    /// </summary>
    [TestMethod]
    public async Task Message_ordering_check_compares_timestamps_at_stored_precision()
    {
        var (conversationId, _) = await SeedConversationAsync(messageSequence: 1, created: SevenDigitInstant, content: "second");

        await using var db = CreateContext();
        db.NotebookConversationMessages.Add(new NotebookConversationMessage
        {
            NotebookConversationId = conversationId,
            Role = ChatRole.User,
            Content = "first",
            TurnIndex = 0,
            MessageSequence = 0,
            Created = SevenDigitInstant,
        });
        await db.SaveChangesAsync();

        (await db.NotebookConversationMessages.CountAsync(m => m.NotebookConversationId == conversationId)).Should().Be(2);
    }

    /// <summary>
    /// Accepted difference: a lower-sequence message less than a microsecond later than a stored higher-sequence one
    /// (the two timestamps are equal once stored on Postgres) is accepted on Postgres and rejected on SQL Server.
    /// </summary>
    [TestMethod]
    public async Task Message_ordering_check_accepts_a_sub_microsecond_inversion_on_postgres_only()
    {
        var (conversationId, _) = await SeedConversationAsync(messageSequence: 1, created: SevenDigitInstant, content: "second");

        await using var db = CreateContext();
        db.NotebookConversationMessages.Add(new NotebookConversationMessage
        {
            NotebookConversationId = conversationId,
            Role = ChatRole.User,
            Content = "first",
            TurnIndex = 0,
            MessageSequence = 0,
            Created = SevenDigitInstant.AddTicks(2),
        });

        var save = () => db.SaveChangesAsync();
        if (IsPostgres)
        {
            await save.Should().NotThrowAsync();
        }
        else
        {
            await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Message timestamps must respect sequence order*");
        }
    }

    /// <summary>A genuinely later timestamp on a lower sequence is still rejected on both providers.</summary>
    [TestMethod]
    public async Task Message_ordering_check_still_rejects_a_later_timestamp()
    {
        var (conversationId, _) = await SeedConversationAsync(messageSequence: 1, created: SevenDigitInstant, content: "second");

        await using var db = CreateContext();
        db.NotebookConversationMessages.Add(new NotebookConversationMessage
        {
            NotebookConversationId = conversationId,
            Role = ChatRole.User,
            Content = "first",
            TurnIndex = 0,
            MessageSequence = 0,
            Created = SevenDigitInstant.AddTicks(10),
        });

        var save = () => db.SaveChangesAsync();
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Message timestamps must respect sequence order*");
    }

    // ------------------------------------------------------------------
    // NUL characters
    // ------------------------------------------------------------------

    /// <summary>Review Focus #3, accepted difference: Postgres stores U+0000 as U+FFFD.</summary>
    [TestMethod]
    public async Task Nul_characters_save()
    {
        var (_, messageId) = await SeedConversationAsync(messageSequence: 0, created: DateTime.UtcNow, content: "a\u0000b");

        await using var db = CreateContext();
        var content = await db.NotebookConversationMessages.Where(m => m.Id == messageId).Select(m => m.Content).SingleAsync();
        content.Should().Be(IsPostgres ? "a�b" : "a\u0000b");
    }

    /// <summary>
    /// A NUL in a column with a unique, case-insensitive index (<c>Users.Email</c>) and in a query parameter: the save and the
    /// lookup work on both providers, and case still doesn't matter.
    /// </summary>
    [TestMethod]
    public async Task Nul_characters_in_a_unique_indexed_column_and_a_parameter()
    {
        var userId = Guid.NewGuid();
        var email = $"nul\u0000{userId:N}@example.com";
        var user = NewUser(userId);
        user.Email = email;
        user.Name = "Nul\u0000Name";
        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var upper = email.ToUpperInvariant();
        var prefix = "NUL\u0000";
        var infix = "l\u0000n";
        await using var read = CreateContext();
        (await read.Users.CountAsync(u => u.Email == upper)).Should().Be(1);
        (await read.Users.CountAsync(u => u.Id == userId && u.Name.StartsWith(prefix))).Should().Be(1);
        (await read.Users.CountAsync(u => u.Id == userId && u.Name.Contains(infix))).Should().Be(1);
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        stored.Email.Should().Be(IsPostgres ? email.Replace('\u0000', '�') : email);
    }

    /// <summary>Text without a NUL is stored as it is, including U+FFFD itself and other control characters.</summary>
    [TestMethod]
    public async Task Text_without_nul_characters_is_stored_unchanged()
    {
        const string text = "plain � \u0001\u001f\t\r\n \\0 'quote' \"dq\" é ß 漢字 🐜 é";
        var (_, messageId) = await SeedConversationAsync(messageSequence: 0, created: DateTime.UtcNow, content: text);

        await using var db = CreateContext();
        var content = await db.NotebookConversationMessages.Where(m => m.Id == messageId).Select(m => m.Content).SingleAsync();
        content.Should().Be(text);
        (await db.NotebookConversationMessages.CountAsync(m => m.Id == messageId && m.Content == text)).Should().Be(1);
    }

    // ------------------------------------------------------------------
    // String length
    // ------------------------------------------------------------------

    /// <summary>
    /// SQL Server raises "String or binary data would be truncated" (2628) for a value longer than its column; Postgres
    /// raises 22001 for <c>character varying(n)</c>. Neither driver truncates the value on the way in.
    /// <c>Users.Email</c> is unbounded in the model and bounded at 450 because of its unique index;
    /// <c>Users.Name</c> is <c>[StringLength(255)]</c>.
    /// </summary>
    [TestMethod]
    public async Task String_longer_than_its_column_is_rejected()
    {
        var longEmail = NewUser();
        longEmail.Email = EmailOfLength(451, longEmail.Id);
        await AssertTooLongAsync(longEmail);

        var longName = NewUser();
        longName.Name = new string('n', 256);
        await AssertTooLongAsync(longName);

        await using var read = CreateContext();
        (await read.Users.CountAsync(u => u.Id == longEmail.Id || u.Id == longName.Id)).Should().Be(0);
    }

    [TestMethod]
    public async Task String_of_exactly_its_column_length_is_stored_whole()
    {
        var user = NewUser();
        user.Email = EmailOfLength(450, user.Id);
        user.Name = new string('n', 255);
        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.Email.Should().Be(user.Email).And.HaveLength(450);
        stored.Name.Should().HaveLength(255);
    }

    // ------------------------------------------------------------------
    // Row versions
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Concurrent_update_throws()
    {
        var section = await SeedSettingAsync();

        await using var first = CreateContext();
        await using var second = CreateContext();
        var a = await first.ApplicationSettings.SingleAsync(s => s.SectionName == section);
        var b = await second.ApplicationSettings.SingleAsync(s => s.SectionName == section);

        a.JsonValue = """{"writer":"first"}""";
        await first.SaveChangesAsync();

        b.JsonValue = """{"writer":"second"}""";
        var save = () => second.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var read = CreateContext();
        (await read.ApplicationSettings.SingleAsync(s => s.SectionName == section)).JsonValue.Should().Be("""{"writer":"first"}""");
    }

    [TestMethod]
    public async Task RowVersion_changes_on_every_update()
    {
        var section = "RowVersionProbe" + Guid.NewGuid().ToString("N")[..8];
        _sectionNames.Add(section);

        await using var db = CreateContext();
        var setting = new ApplicationSetting { SectionName = section, JsonValue = "{}" };
        db.ApplicationSettings.Add(setting);
        await db.SaveChangesAsync();

        // The insert returns a usable token: the one tracked is the one stored. xmin is 4 bytes, rowversion 8.
        var versions = new List<string> { Convert.ToBase64String(setting.RowVersion) };
        setting.RowVersion.Should().HaveCount(IsPostgres ? 4 : 8).And.NotEqual(new byte[setting.RowVersion.Length]);
        (await StoredRowVersionAsync(section)).Should().Be(versions[0]);

        for (var i = 1; i <= 3; i++)
        {
            setting.JsonValue = $$"""{"update":{{i}}}""";
            await db.SaveChangesAsync();
            var version = Convert.ToBase64String(setting.RowVersion);
            versions.Should().NotContain(version);
            (await StoredRowVersionAsync(section)).Should().Be(version);
            versions.Add(version);
        }
    }

    /// <summary>
    /// A stale token from an earlier read conflicts, and so does a token of the other provider's length (an 8-byte
    /// token converts to xmin 0 on Postgres, which no row has).
    /// </summary>
    [TestMethod]
    public async Task Stale_or_foreign_row_version_conflicts()
    {
        var section = await SeedSettingAsync();
        byte[] stale;
        await using (var db = CreateContext())
        {
            var setting = await db.ApplicationSettings.SingleAsync(s => s.SectionName == section);
            stale = setting.RowVersion;
            setting.JsonValue = """{"bumped":true}""";
            await db.SaveChangesAsync();
        }

        foreach (var token in new[] { stale, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, new byte[] { 0, 0, 0, 1 } })
        {
            await using var db = CreateContext();
            var setting = await db.ApplicationSettings.SingleAsync(s => s.SectionName == section);
            db.Entry(setting).Property(s => s.RowVersion).OriginalValue = token;
            setting.JsonValue = """{"stale":true}""";

            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateConcurrencyException>($"token {Convert.ToBase64String(token)} is stale");
        }

        await using var read = CreateContext();
        (await read.ApplicationSettings.SingleAsync(s => s.SectionName == section)).JsonValue.Should().Be("""{"bumped":true}""");
    }

    /// <summary>
    /// Every <c>SaveChanges</c> inside a user transaction gets a new token on both providers: EF wraps each such save
    /// in a savepoint, and on Postgres the savepoint's subtransaction id becomes the row's <c>xmin</c>. A token read
    /// before the transaction conflicts after it.
    /// </summary>
    [TestMethod]
    public async Task RowVersion_changes_on_every_save_inside_a_transaction()
    {
        var section = await SeedSettingAsync();
        byte[] before, afterFirst, afterSecond;

        await using (var db = CreateContext())
        {
            var setting = await db.ApplicationSettings.SingleAsync(s => s.SectionName == section);
            before = setting.RowVersion;

            // The retrying execution strategy only allows a user transaction inside ExecuteAsync.
            (afterFirst, afterSecond) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                setting.JsonValue = """{"step":1}""";
                await db.SaveChangesAsync();
                var first = setting.RowVersion;
                setting.JsonValue = """{"step":2}""";
                await db.SaveChangesAsync();
                var second = setting.RowVersion;
                await transaction.CommitAsync();
                return (first, second);
            });
        }

        afterFirst.Should().NotEqual(before);
        afterSecond.Should().NotEqual(afterFirst);
        (await StoredRowVersionAsync(section)).Should().Be(Convert.ToBase64String(afterSecond));
        await AssertStaleTokenConflictsAsync(section, before);
    }

    /// <summary>
    /// Accepted difference: <c>rowversion</c> changes on every update statement, <c>xmin</c> once per transaction (or
    /// savepoint). Two set-based updates of one row in one transaction, with no savepoint between them, leave Postgres
    /// with one new token and SQL Server with two. A token read before the transaction conflicts after it on both.
    /// </summary>
    [TestMethod]
    public async Task Two_set_based_updates_in_one_transaction_give_one_token_on_postgres_and_two_on_sql_server()
    {
        var section = await SeedSettingAsync();
        var before = Convert.FromBase64String(await StoredRowVersionAsync(section));
        string afterFirst, afterSecond;

        await using (var db = CreateContext())
        {
            (afterFirst, afterSecond) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var row = db.ApplicationSettings.Where(s => s.SectionName == section);
                await row.ExecuteUpdateAsync(s => s.SetProperty(x => x.SchemaVersion, 2));
                var first = Convert.ToBase64String(await row.Select(x => x.RowVersion).SingleAsync());
                await row.ExecuteUpdateAsync(s => s.SetProperty(x => x.SchemaVersion, 3));
                var second = Convert.ToBase64String(await row.Select(x => x.RowVersion).SingleAsync());
                await transaction.CommitAsync();
                return (first, second);
            });
        }

        afterFirst.Should().NotBe(Convert.ToBase64String(before));
        if (IsPostgres)
        {
            afterSecond.Should().Be(afterFirst);
        }
        else
        {
            afterSecond.Should().NotBe(afterFirst);
        }

        (await StoredRowVersionAsync(section)).Should().Be(afterSecond);
        await AssertStaleTokenConflictsAsync(section, before);
    }

    /// <summary>
    /// Review Focus #5: the client sends the section's <c>RowVersion</c> back as opaque base64. Saving twice with the
    /// first response's version returns 409 the second time.
    /// </summary>
    [TestMethod]
    public async Task Settings_endpoint_rejects_stale_row_version()
    {
        var section = await Client.GetFromJsonAsync<SettingsSectionDto>("/api/settings/sections/ChatDefaults");
        section.Should().NotBeNull();
        var original = section!.RowVersion;
        Convert.FromBase64String(original).Should().HaveCount(IsPostgres ? 4 : 8);

        var first = await Client.PutAsJsonAsync(
            "/api/settings/sections/ChatDefaults",
            new { rowVersion = original, payload = new { OverrideAllChatModels = false } });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await first.Content.ReadFromJsonAsync<SettingsSectionDto>();
        saved!.RowVersion.Should().NotBe(original);

        var second = await Client.PutAsJsonAsync(
            "/api/settings/sections/ChatDefaults",
            new { rowVersion = original, payload = new { OverrideAllChatModels = false } });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var current = await Client.PutAsJsonAsync(
            "/api/settings/sections/ChatDefaults",
            new { rowVersion = saved.RowVersion, payload = new { OverrideAllChatModels = false } });
        current.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private ApplicationDbContext CreateContext() => new(_options);

    private User NewUser(Guid? id = null)
    {
        var userId = id ?? Guid.NewGuid();
        _userIds.Add(userId);
        return new User { Id = userId, Name = "Date behavior", Email = $"date-behavior.{userId:N}@example.com" };
    }

    private async Task<Guid> SeedUserAsync(DateTime created)
    {
        var user = NewUser();
        user.Created = created;
        await using var db = CreateContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static string EmailOfLength(int length, Guid id)
    {
        var suffix = $".{id:N}@example.com";
        return new string('e', length - suffix.Length) + suffix;
    }

    private async Task AssertTooLongAsync(User user)
    {
        await using var db = CreateContext();
        db.Users.Add(user);

        var save = () => db.SaveChangesAsync();
        var thrown = await save.Should().ThrowAsync<DbUpdateException>();
        var inner = thrown.Which.InnerException;
        if (IsPostgres)
        {
            inner.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.StringDataRightTruncation);
        }
        else
        {
            inner.Should().BeOfType<SqlException>().Which.Number.Should().BeOneOf(2628, 8152);
        }
    }

    private async Task<string> SeedSettingAsync()
    {
        var section = "RowVersionProbe" + Guid.NewGuid().ToString("N")[..8];
        _sectionNames.Add(section);
        await using var db = CreateContext();
        db.ApplicationSettings.Add(new ApplicationSetting { SectionName = section, JsonValue = """{"seed":true}""" });
        await db.SaveChangesAsync();
        return section;
    }

    private async Task AssertStaleTokenConflictsAsync(string section, byte[] staleToken)
    {
        await using var db = CreateContext();
        var row = await db.ApplicationSettings.SingleAsync(s => s.SectionName == section);
        db.Entry(row).Property(s => s.RowVersion).OriginalValue = staleToken;
        row.JsonValue = """{"stale":true}""";
        var save = () => db.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private async Task<string> StoredRowVersionAsync(string section)
    {
        await using var db = CreateContext();
        var rowVersion = await db.ApplicationSettings.Where(s => s.SectionName == section).Select(s => s.RowVersion).SingleAsync();
        return Convert.ToBase64String(rowVersion);
    }

    /// <summary>A project, notebook and conversation with turn 0 and one message in it.</summary>
    private async Task<(Guid ConversationId, Guid MessageId)> SeedConversationAsync(int messageSequence, DateTime created, string content)
    {
        var projectId = Guid.NewGuid();
        _projectIds.Add(projectId);
        var notebook = new Notebook { Title = "Date behavior notebook", Slug = $"date-{Guid.NewGuid():N}", ProjectId = projectId };
        var conversation = new NotebookConversation { NotebookId = notebook.Id, Title = "Date behavior conversation" };
        var message = new NotebookConversationMessage
        {
            NotebookConversationId = conversation.Id,
            Role = ChatRole.Assistant,
            Content = content,
            TurnIndex = 0,
            MessageSequence = messageSequence,
            Created = created,
        };

        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = projectId, Title = "Date behavior project", Slug = $"date-{projectId:N}" });
        db.Notebooks.Add(notebook);
        db.NotebookConversations.Add(conversation);
        db.ConversationTurns.Add(new ConversationTurn
        {
            NotebookConversationId = conversation.Id,
            TurnIndex = 0,
            AssistantName = "Date behavior assistant",
            Instructions = "Answer."
        });
        db.NotebookConversationMessages.Add(message);
        await db.SaveChangesAsync();
        return (conversation.Id, message.Id);
    }
}
