using System.Data;
using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Services.Conversations.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Stop's fence (<c>ConversationPersistence.FenceTurnCancellationAsync</c>) racing a worker that is committing a new
/// streaming assistant row (parity report #55). The worker's write inserts the row and touches the turn in one
/// transaction, fenced on the turn's <c>Status</c> and <c>ExecutionId</c>. The fence reads the turn's streaming rows,
/// clears their <c>IsStreaming</c>, then updates the turn. Under row versioning the reads don't wait for the worker:
/// only the fence's update of the turn waits for the worker's row lock, so a row the worker commits meanwhile was missed
/// and stayed streaming. That is Postgres (MVCC), and also SQL Server with <c>READ_COMMITTED_SNAPSHOT</c> on.
/// The suite's SQL Server database has it on (<c>is_read_committed_snapshot_on = 1</c>) because EF Core created that
/// database: when EF creates a database (<c>Migrate</c>, <c>EnsureCreated</c>) it also sends
/// <c>IF SERVERPROPERTY('EngineEdition') &lt;&gt; 5 ... ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON</c>, so on
/// every edition but Azure SQL Database, which has it on by default anyway. There the test failed the same way, the
/// fence waiting with <c>LCK_M_U</c> on the turn row in its <c>SaveChanges</c>. A database created by the app's own
/// <c>SqlServerDatabaseInitializer</c> (a plain <c>CREATE DATABASE</c>, then <c>RECOVERY SIMPLE</c>) has it off. With
/// it off, the fence's first read of the turn waits for the worker's uncommitted touch and sees the row, so the race is
/// narrower but not closed: a worker write that falls entirely between that read and the fence's turn update could
/// still escape (reasoned, not run). The fix is on the Postgres path only (SQL Server's statements are unchanged), so
/// the test is Postgres-only. Runs the real fence against the suite's database (no web host).
/// </summary>
[TestClass]
public sealed class StopFenceBehaviorTests
{
    private string _connectionString = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private ServiceProvider _services = null!;
    private readonly List<Guid> _projectIds = [];

    [TestInitialize]
    public async Task TestInitialize()
    {
        _connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var tuning = new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true);
        // The test's own contexts open a transaction by hand (the worker's), which a retrying strategy rejects.
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder, TestContainerManager.Instance.Provider, _connectionString, tuning with { RetryOnFailure = false });
        _options = builder.Options;

        // The host's registration of the context that ConversationPersistence resolves per call.
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            ApplicationDbContextOptions.Configure(o, TestContainerManager.Instance.Provider, _connectionString, tuning));
        _services = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await using (var db = CreateContext())
        {
            await db.NotebookConversationMessages
                .Where(m => _projectIds.Contains(m.NotebookConversation.Notebook.ProjectId))
                .ExecuteDeleteAsync();
            await db.Notebooks.Where(n => _projectIds.Contains(n.ProjectId)).ExecuteDeleteAsync();
            await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        await _services.DisposeAsync();
    }

    /// <summary>
    /// The worker has touched the turn and inserted a streaming row, and hasn't committed. The fence runs, waits for the
    /// worker (the test checks that the database reports it blocked by the worker's session), and the worker then
    /// commits. No streaming assistant row may be left on the cancelled turn: before the fix, the fence's earlier read
    /// missed the uncommitted row and left it <c>IsStreaming = true</c> for good (recovery only sweeps turns still
    /// <c>streaming</c>).
    /// </summary>
    [TestMethod]
    public async Task Stop_fence_clears_a_streaming_row_the_worker_commits_while_the_fence_waits()
    {
        DatabaseTestProvider.RequirePostgres();

        var (conversationId, turnId, executionId) = await SeedStreamingTurnAsync();
        var persistence = new ConversationPersistence(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConversationPersistence>.Instance);

        await using var worker = CreateContext();
        await using var workerTransaction = await worker.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        (await worker.ConversationTurns
                .Where(t => t.Id == turnId && t.Status == "streaming" && t.ExecutionId == executionId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUpdated, DateTime.UtcNow)))
            .Should().Be(1, "the worker's write is fenced on the turn's status and execution id");
        worker.NotebookConversationMessages.Add(new NotebookConversationMessage
        {
            NotebookConversationId = conversationId,
            TurnIndex = 0,
            MessageSequence = 2,
            Role = ChatRole.Assistant,
            AssistantName = "Fence assistant",
            Content = string.Empty,
            IsStreaming = true
        });
        await worker.SaveChangesAsync();
        var workerSession = await SessionIdAsync(worker);

        var fence = Task.Run(() => persistence.FenceTurnCancellationAsync(conversationId, turnId));
        await WaitUntilBlockedByAsync(workerSession, fence);
        await workerTransaction.CommitAsync();
        var result = await fence;

        result.Found.Should().BeTrue();
        result.Status.Should().Be("cancelled");
        await using var verify = CreateContext();
        var assistantRows = await verify.NotebookConversationMessages
            .Where(m => m.NotebookConversationId == conversationId && m.Role == ChatRole.Assistant)
            .Select(m => m.IsStreaming)
            .ToListAsync();
        assistantRows.Should().ContainSingle("the worker's row is committed");
        assistantRows.Should().NotContain(true, "a cancelled turn must not keep a streaming row");
        (await verify.ConversationTurns.Where(t => t.Id == turnId).Select(t => t.Status).SingleAsync())
            .Should().Be("cancelled");
    }

    private ApplicationDbContext CreateContext() => new(_options);

    private async Task<(Guid ConversationId, Guid TurnId, Guid ExecutionId)> SeedStreamingTurnAsync()
    {
        var project = new Project { Title = "Fence test project", Slug = $"fence-{Guid.NewGuid():N}" };
        _projectIds.Add(project.Id);
        var notebook = new Notebook { Title = "Fence notebook", Slug = $"fence-{Guid.NewGuid():N}", ProjectId = project.Id };
        var conversation = new NotebookConversation { NotebookId = notebook.Id, Title = "Fence conversation" };
        var executionId = Guid.NewGuid();
        var turn = new ConversationTurn
        {
            NotebookConversationId = conversation.Id,
            TurnIndex = 0,
            AssistantName = "Fence assistant",
            Instructions = "Answer.",
            Status = "streaming",
            ExecutionId = executionId
        };
        var userMessage = new NotebookConversationMessage
        {
            NotebookConversationId = conversation.Id,
            TurnIndex = 0,
            MessageSequence = 1,
            Role = ChatRole.User,
            Content = "Hello."
        };

        await using var db = CreateContext();
        db.Projects.Add(project);
        db.Notebooks.Add(notebook);
        db.NotebookConversations.Add(conversation);
        db.ConversationTurns.Add(turn);
        db.NotebookConversationMessages.Add(userMessage);
        await db.SaveChangesAsync();
        return (conversation.Id, turn.Id, executionId);
    }

    /// <summary>The database session id of the context's connection (<c>pg_backend_pid()</c> or <c>@@SPID</c>).</summary>
    private static async Task<int> SessionIdAsync(ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = db.Database.IsNpgsql() ? "SELECT pg_backend_pid()" : "SELECT CAST(@@SPID AS int)";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>Waits until another session is blocked by <paramref name="sessionId"/>, while the fence is still running.</summary>
    private async Task WaitUntilBlockedByAsync(int sessionId, Task fence)
    {
        await using var probe = CreateContext();
        var connection = probe.Database.GetDbConnection();
        await connection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            fence.IsCompleted.Should().BeFalse("the fence must wait for the worker's uncommitted write");
            await using var command = connection.CreateCommand();
            command.CommandText = probe.Database.IsNpgsql()
                ? $"SELECT count(*)::int FROM pg_stat_activity WHERE {sessionId} = ANY(pg_blocking_pids(pid))"
                : $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {sessionId}";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"No session waited for the worker's session {sessionId}.");
    }
}
