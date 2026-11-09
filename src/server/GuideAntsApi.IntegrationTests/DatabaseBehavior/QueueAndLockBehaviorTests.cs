using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.Database;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Services.Auth;
using GuideAntsApi.Services.Conversations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// The concurrency primitives that used to be T-SQL only (parity report rows #17, #18, #21 and the serializable
/// retry row): the background job claim, the transaction-owned application lock (<c>sp_getapplock</c> on SQL Server,
/// a polled transaction-level advisory lock on Postgres), the two serializable sections that take that lock
/// (registration and the admin user mutations) and foreign key violation detection. The tests run on both providers
/// against the suite's database and expect the SQL Server outcome.
/// </summary>
/// <remarks>
/// Jobs use job types unique to the test, so the background processors of other test hosts (which claim only their
/// registered job types) never take them. The registration and admin tests empty the <c>Users</c> table the way
/// <c>AuthEndpointsTests</c> does, because the first user becomes the admin and the last-admin guard counts every
/// admin.
/// </remarks>
[TestClass]
public sealed class QueueAndLockBehaviorTests
{
    private const string TestLock = "Test.Lock";
    private const string AssignedByForeignKey = "FK_UserRoles_Users_AssignedByUserId";
    private const string ConversationLockForeignKey = "FK_ConversationLocks_NotebookConversations_ConversationId";

    private static readonly DateTime T0 = new(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddMinutes(1);

    private static TestWebApplicationFactory? _factory;

    private string _connectionString = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly List<string> _jobTypes = [];
    private readonly List<Guid> _userIds = [];

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null;
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        _connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder,
            TestContainerManager.Instance.Provider,
            _connectionString,
            new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));
        _options = builder.Options;
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await using var db = CreateContext();
        if (_jobTypes.Count > 0)
        {
            await db.JobQueue.Where(j => _jobTypes.Contains(j.JobType)).ExecuteDeleteAsync();
        }

        if (_userIds.Count > 0)
        {
            await db.UserRoles.Where(r => _userIds.Contains(r.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }

    // ----- Background job claim (#21) -----

    /// <summary>
    /// Eight claimers race for two jobs. A claimer that loses the race for a row gets no job (it never takes a job
    /// someone else holds), so the claimers run in rounds until a round finds nothing: across all rounds exactly the
    /// two jobs are claimed, each once, and each row carries the claim token of the claimer that got it.
    /// </summary>
    [TestMethod]
    public async Task Concurrent_claims_never_double_claim()
    {
        var jobType = NewJobType();
        var first = await SeedJobAsync(jobType, priority: 0, created: T0);
        var second = await SeedJobAsync(jobType, priority: 0, created: T1);
        var queue = CreateJobQueue();

        var claimed = new List<JobQueue>();
        var firstRound = -1;
        for (var round = 0; round < 10; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var claims = Enumerable.Range(0, 8)
                .Select(async _ =>
                {
                    await start.Task;
                    return await queue.TryClaimAsync(jobType, leaseSeconds: 60);
                })
                .ToList();
            start.SetResult();
            var results = (await Task.WhenAll(claims)).Where(r => r is not null).Select(r => r!).ToList();
            if (firstRound < 0)
            {
                firstRound = results.Count;
            }

            if (results.Count == 0)
            {
                break;
            }

            claimed.AddRange(results);
        }

        firstRound.Should().BeInRange(1, 2, "a round of claimers takes at least one of the free jobs and no more than exist");
        claimed.Should().HaveCount(2);
        claimed.Select(j => j.Id).Should().BeEquivalentTo([first, second]);
        claimed.Select(j => j.ClaimToken).Should().OnlyHaveUniqueItems();

        await using var db = CreateContext();
        var rows = await db.JobQueue.AsNoTracking().Where(j => j.JobType == jobType).ToListAsync();
        rows.Should().OnlyContain(j => j.Status == JobStatus.Processing);
        rows.ToDictionary(j => j.Id, j => j.ClaimToken)
            .Should().BeEquivalentTo(claimed.ToDictionary(j => j.Id, j => j.ClaimToken));
    }

    [TestMethod]
    public async Task Claim_order_is_priority_then_created()
    {
        var jobType = NewJobType();
        var lowOld = await SeedJobAsync(jobType, priority: 1, created: T0);
        var highNew = await SeedJobAsync(jobType, priority: 5, created: T1);
        var highOld = await SeedJobAsync(jobType, priority: 5, created: T0);
        var queue = CreateJobQueue();

        var order = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var job = await queue.TryClaimAsync(jobType, leaseSeconds: 60);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Processing);
            order.Add(job.Id);
        }

        order.Should().Equal(highOld, highNew, lowOld);
        (await queue.TryClaimAsync(jobType, leaseSeconds: 60)).Should().BeNull();
    }

    /// <summary>
    /// A typed claim takes only its type, even over a higher-priority job of another type; an untyped claim takes
    /// the highest-priority job of any type (the test job has the highest possible priority, so it wins over any
    /// other test's leftover job).
    /// </summary>
    [TestMethod]
    public async Task Claim_filters_by_job_type_or_any()
    {
        var typeA = NewJobType();
        var typeB = NewJobType();
        var a = await SeedJobAsync(typeA, priority: 0, created: T0);
        var b = await SeedJobAsync(typeB, priority: int.MaxValue, created: T0);
        var queue = CreateJobQueue();

        (await queue.TryClaimAsync(NewJobType(), leaseSeconds: 60)).Should().BeNull();
        (await queue.TryClaimAsync(typeA, leaseSeconds: 60))!.Id.Should().Be(a);
        (await queue.TryClaimAsync(typeA, leaseSeconds: 60)).Should().BeNull();
        (await queue.TryClaimAsync(null, leaseSeconds: 60))!.Id.Should().Be(b);
    }

    /// <summary>
    /// The claim catches a provider error, logs it and reports no job, on both providers. A database without the
    /// <c>JobQueue</c> table (<c>master</c> on SQL Server, <c>postgres</c> on Postgres) gives a deterministic,
    /// non-transient error: SQL Server 208, Postgres 42P01.
    /// </summary>
    [TestMethod]
    public async Task Claim_logs_a_provider_error_and_returns_no_job()
    {
        var connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var provider = TestContainerManager.Instance.Provider;
        var otherDatabase = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString
            : new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder, provider, otherDatabase, new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));

        var logger = new ListLogger<JobQueueService>();
        var queue = new JobQueueService(
            new OptionsContextFactory(builder.Options),
            new NoActiveJobs(),
            Microsoft.Extensions.Options.Options.Create(new JobRetryOptions()),
            logger);

        (await queue.TryClaimAsync(NewJobType(), leaseSeconds: 60)).Should().BeNull();

        var expected = provider == DatabaseProvider.SqlServer
            ? "Job claim failed with SqlException Number=208 State=1"
            : "Job claim failed with PostgresException Number=42P01 State=";
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.Message.Should().Be(expected);
    }

    // ----- Application lock (#17) -----

    [TestMethod]
    public async Task App_lock_serializes_holders()
    {
        var aHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long aCommitStarted = 0;

        var a = Task.Run(() => InTransactionAsync(async db =>
        {
            await DatabaseAppLock.AcquireTransactionLockAsync(db.Database, TestLock, TimeSpan.FromSeconds(10), CancellationToken.None);
            aHolds.SetResult();
            await aRelease.Task;
            aCommitStarted = Stopwatch.GetTimestamp();
            return 0;
        }));
        await aHolds.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var bStarted = Stopwatch.GetTimestamp();
        var b = Task.Run(() => InTransactionAsync(async db =>
        {
            await DatabaseAppLock.AcquireTransactionLockAsync(db.Database, TestLock, TimeSpan.FromSeconds(10), CancellationToken.None);
            return Stopwatch.GetTimestamp();
        }));

        await Task.Delay(500);
        b.IsCompleted.Should().BeFalse("B waits while A holds the lock");
        aRelease.SetResult();
        await a;
        var bAcquired = await b;

        bAcquired.Should().BeGreaterThanOrEqualTo(aCommitStarted, "B gets the lock only once A commits");
        Stopwatch.GetElapsedTime(bStarted, bAcquired).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// <c>sp_getapplock</c> returns -1 when its timeout passes, and the callers ignore the return value, so the
    /// transaction carries on without the lock. The Postgres lock does the same: no exception, and the transaction
    /// stays usable.
    /// </summary>
    [TestMethod]
    public async Task App_lock_timeout_proceeds_like_sp_getapplock()
    {
        var aHolds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = Task.Run(() => InTransactionAsync(async db =>
        {
            await DatabaseAppLock.AcquireTransactionLockAsync(db.Database, TestLock, TimeSpan.FromSeconds(10), CancellationToken.None);
            aHolds.SetResult();
            await aRelease.Task;
            return 0;
        }));
        await aHolds.Task.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            var logger = new ListLoggerProvider();
            var (waited, users) = await InTransactionAsync(async db =>
            {
                var stopwatch = Stopwatch.StartNew();
                await DatabaseAppLock.AcquireTransactionLockAsync(db.Database, TestLock, TimeSpan.FromMilliseconds(200), CancellationToken.None);
                var waited = stopwatch.Elapsed;
                return (waited, await db.Users.CountAsync());
            }, logger);

            waited.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(200));
            waited.Should().BeLessThan(TimeSpan.FromSeconds(5));
            users.Should().BeGreaterThanOrEqualTo(0);
            if (TestContainerManager.Instance.Provider == DatabaseProvider.Postgres)
            {
                logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Category == typeof(DatabaseAppLock).FullName)
                    .Which.Message.Should().Contain(TestLock);
            }
        }
        finally
        {
            aRelease.TrySetResult();
            await a;
        }
    }

    // ----- Serializable sections that take the lock (#17, serializable retry row) -----

    /// <summary>
    /// Review Focus #2. On SQL Server the second registration waits for the lock and then sees the first user. On
    /// Postgres its serializable snapshot is taken by the lock statement, before the first commit, so the second one
    /// fails with a serialization failure (40001) and must be retried with a clean change tracker. Either way both
    /// succeed: one Admin, one Pending, never a 409 or a 500.
    /// </summary>
    [TestMethod]
    public async Task Concurrent_registrations_both_succeed()
    {
        var factory = await GetFactoryAsync();

        for (var round = 0; round < 3; round++)
        {
            await ResetUsersAsync(factory);
            using var first = factory.CreateClient();
            using var second = factory.CreateClient();

            var responses = await Task.WhenAll(
                first.PostAsJsonAsync("/api/auth/register", new
                {
                    name = "Concurrent One",
                    email = $"concurrent.one.{round}@example.com",
                    password = "Password123!"
                }),
                second.PostAsJsonAsync("/api/auth/register", new
                {
                    name = "Concurrent Two",
                    email = $"concurrent.two.{round}@example.com",
                    password = "Password123!"
                }));

            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            }

            var roles = new List<string>();
            foreach (var response in responses)
            {
                roles.Add((await response.Content.ReadFromJsonAsync<AuthResponse>())!.Role);
            }

            roles.Should().BeEquivalentTo([Role.Admin.ToString(), Role.Pending.ToString()]);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserRoles.Select(r => r.Role).ToListAsync()).Should().BeEquivalentTo([Role.Admin, Role.Pending]);
            (await db.Users.CountAsync()).Should().Be(2);
        }

        await ResetUsersAsync(factory);
    }

    /// <summary>
    /// The admin mutations run the same way (serializable transaction, then the lock). Two admins demote each other
    /// at once: one demotion wins and the other hits the last-admin guard, so exactly one admin is left. On Postgres
    /// the loser is retried after a serialization failure; without a clean change tracker the retry would reuse its
    /// own stale, already-demoted role row and remove the last admin.
    /// </summary>
    [TestMethod]
    public async Task Concurrent_admin_demotions_keep_one_admin()
    {
        var factory = await GetFactoryAsync();

        for (var round = 0; round < 3; round++)
        {
            await ResetUsersAsync(factory);
            var adminA = await SeedAdminAsync(factory, $"admin.a.{round}@example.com");
            var adminB = await SeedAdminAsync(factory, $"admin.b.{round}@example.com");

            using var scopeA = factory.Services.CreateScope();
            using var scopeB = factory.Services.CreateScope();
            var serviceA = scopeA.ServiceProvider.GetRequiredService<IAdminUserService>();
            var serviceB = scopeB.ServiceProvider.GetRequiredService<IAdminUserService>();

            var results = await Task.WhenAll(
                Task.Run(() => serviceA.ChangeRoleAsync(adminB, Role.Reader.ToString(), adminA)),
                Task.Run(() => serviceB.ChangeRoleAsync(adminA, Role.Reader.ToString(), adminB)));

            results.Count(r => r.IsSuccess).Should().Be(1);
            var loser = results.Single(r => !r.IsSuccess);
            loser.Failure!.Error.Should().Be(AdminUserOperationError.Guarded);
            loser.Failure.Message.Should().Be("Cannot demote the last active admin.");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserRoles.CountAsync(r => r.Role == Role.Admin)).Should().Be(1);
        }

        await ResetUsersAsync(factory);
    }

    // ----- Foreign key violation detection (#18) -----

    /// <summary>The conversation lock's foreign key is checked at the insert, so a missing conversation is NotFound.</summary>
    [TestMethod]
    public async Task Lock_for_missing_conversation_returns_NotFound()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => ApplicationDbContextOptions.Configure(
            o,
            TestContainerManager.Instance.Provider,
            _connectionString,
            new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true)));
        await using var provider = services.BuildServiceProvider();
        var locks = new DistributedConversationLockService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DistributedConversationLockService>.Instance);

        var result = await locks.TryAcquireLockAsync(Guid.NewGuid(), "queue-and-lock-test");

        result.Status.Should().Be(LockAcquisitionStatus.ConversationNotFound);

        if (TestContainerManager.Instance.Provider == DatabaseProvider.Postgres)
        {
            await using var db = CreateContext();
            var deferrable = await db.Database
                .SqlQuery<bool>($"SELECT condeferrable AS \"Value\" FROM pg_constraint WHERE conname = {ConversationLockForeignKey}")
                .ToListAsync();
            deferrable.Should().Equal([false], "the insert-time check is what turns a missing conversation into NotFound");
        }
    }

    /// <summary>
    /// Both shapes <see cref="DatabaseErrors.IsForeignKeyViolation"/> must read. SQL Server rejects the insert, so
    /// <c>SaveChanges</c> throws a <see cref="DbUpdateException"/> wrapping error 547. On Postgres this foreign key is
    /// deferred (#25), so <c>SaveChanges</c> succeeds and the commit throws a bare <see cref="PostgresException"/>
    /// 23503.
    /// </summary>
    [TestMethod]
    public async Task Foreign_key_violation_is_recognized_wrapped_or_raised_by_the_commit()
    {
        var userId = Guid.NewGuid();
        _userIds.Add(userId);
        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User { Id = userId, Name = "Queue and lock user", Email = $"fk-{userId:N}@queue.local" });
            await seed.SaveChangesAsync();
        }

        Exception? error = null;
        await using (var db = CreateContext())
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.UserRoles.Add(new UserRole { UserId = userId, Role = Role.Reader, AssignedByUserId = Guid.NewGuid() });
                try
                {
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
        }

        error.Should().NotBeNull();
        if (TestContainerManager.Instance.Provider == DatabaseProvider.SqlServer)
        {
            error.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<SqlException>();
        }
        else
        {
            error.Should().BeOfType<PostgresException>();
        }

        DatabaseErrors.IsForeignKeyViolation(error!, AssignedByForeignKey).Should().BeTrue();
        DatabaseErrors.IsForeignKeyViolation(error!, ConversationLockForeignKey).Should().BeFalse();
        DatabaseErrors.TryGetProviderError(error!, out var code, out _).Should().BeTrue();
        code.Should().Be(TestContainerManager.Instance.Provider == DatabaseProvider.SqlServer ? "547" : "23503");
    }

    // ----- Helpers -----

    private ApplicationDbContext CreateContext() => new(_options);

    private string NewJobType()
    {
        var jobType = $"QueueAndLock.{Guid.NewGuid():N}";
        _jobTypes.Add(jobType);
        return jobType;
    }

    private async Task<Guid> SeedJobAsync(string jobType, int priority, DateTime created)
    {
        await using var db = CreateContext();
        var job = new JobQueue
        {
            JobType = jobType,
            PayloadJson = "{}",
            Priority = priority,
            AvailableAt = created,
            Created = created,
            UpdatedUtc = created
        };
        db.JobQueue.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private JobQueueService CreateJobQueue() => new(
        new OptionsContextFactory(_options),
        new NoActiveJobs(),
        Microsoft.Extensions.Options.Options.Create(new JobRetryOptions()),
        NullLogger<JobQueueService>.Instance);

    /// <summary>Runs <paramref name="body"/> in its own context and a transaction at the default isolation level, then commits.</summary>
    private async Task<T> InTransactionAsync<T>(Func<ApplicationDbContext, Task<T>> body, ILoggerProvider? logs = null)
    {
        var options = _options;
        if (logs is not null)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>(_options);
            builder.UseLoggerFactory(LoggerFactory.Create(b => b.AddProvider(logs)));
            options = builder.Options;
        }

        await using var db = new ApplicationDbContext(options);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await body(db);
            await transaction.CommitAsync();
            return result;
        });
    }

    private static async Task<TestWebApplicationFactory> GetFactoryAsync()
    {
        if (_factory is null)
        {
            var factory = new TestWebApplicationFactory();
            await factory.InitializeAsync();
            _factory = factory;
        }

        return _factory;
    }

    /// <summary>The same reset as <c>AuthEndpointsTests</c>: no users and no rows that point at one.</summary>
    private static async Task ResetUsersAsync(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.NotebookConversationMessages.ExecuteUpdateAsync(set => set
            .SetProperty(m => m.UserId, (Guid?)null)
            .SetProperty(m => m.LastEditedByUserId, (Guid?)null));
        await db.MessageEditHistories.ExecuteUpdateAsync(set => set.SetProperty(h => h.FirstEditedByUserId, (Guid?)null));
        await db.UserProjectContextOptions.ExecuteDeleteAsync();
        await db.UserRoles.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
    }

    private static async Task<Guid> SeedAdminAsync(TestWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var user = new User { Name = email, Email = email, ApprovedAt = now };
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, Role = Role.Admin, AssignedAt = now });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private sealed record AuthResponse(Guid UserId, string Name, string Email, string Role, bool MustChangePassword);

    private sealed class OptionsContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private sealed class NoActiveJobs : IActiveJobExecutionRegistry
    {
        public void MarkActive(Guid jobId, Guid claimToken) { }

        public void Touch(Guid jobId, Guid claimToken) { }

        public void MarkInactive(Guid jobId, Guid claimToken) { }

        public HashSet<ActiveJobExecutionKey> GetActiveSince(DateTime utcCutoff) => [];
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class ListLogger<T>(List<LogEntry>? sink = null, string? category = null) : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = sink ?? [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add(new LogEntry(category ?? typeof(T).FullName!, logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger<object>(Entries, categoryName);

        public void Dispose() { }
    }
}
