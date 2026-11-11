using System.Diagnostics;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.Services.Conversations.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// Saving one chat turn the way the stream engine does, through the production <see cref="IConversationPersistence"/>:
/// create the turn (streaming, with an execution fence), save the user message, start the streaming assistant message,
/// one streaming checkpoint, finalize the assistant message, and terminalize the turn as completed.
/// </summary>
public sealed class SaveChatTurnOperation(SeedData seed) : IOperation
{
    private const string UserText = "Can you summarize what the notebook's files say about the conversation lock?";
    private const string PartialReply = "The conversation lock keeps two people from";
    private readonly string _reply = string.Concat(Enumerable.Repeat(
        "The conversation lock keeps two people from editing the same conversation at once; the lease is renewed while the turn streams. ", 8));

    private readonly Dictionary<DatabaseProvider, int> _turnsBefore = [];

    public string Name => "save one chat turn";

    public string Description =>
        "`IConversationPersistence` (`ConversationPersistence`; each call opens its own scope and context, as in production), in the engine's order: " +
        "`CreateNextTurnAsync` (status `streaming`, new execution fence), `CreateUserMessageAsync`, `StartAssistantMessageAsync` (streaming), " +
        "`CheckpointTurnAsync` (one streaming checkpoint), `AppendOrFinalizeAssistantMessageAsync` (finalize, about 1 KB) and " +
        "`TerminalizeTurnAsync` (`completed`). The fenced writes carry the turn's `Status`/`ExecutionId` concurrency predicates. " +
        "Every run appends a turn to the same conversation, so it grows by one turn per run on both providers.";

    public IReadOnlyList<string> Metrics => [Name];

    public async Task PrepareAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        _turnsBefore[target.Provider] = await db.ConversationTurns.CountAsync(t => t.NotebookConversationId == seed.TurnSaveConversationId, ct);
    }

    public async Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct)
    {
        await using var scope = target.Services.CreateAsyncScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IConversationPersistence>();
        var conversationId = seed.TurnSaveConversationId;

        var stopwatch = Stopwatch.StartNew();
        var turn = await persistence.CreateNextTurnAsync(
            new CreateTurnRequest(conversationId, SeedData.AssistantName, SeedData.ModelDeploymentId, "You are a helpful assistant.", InitialStatus: "streaming"), ct);
        var execution = turn.Turn.ExecutionId;
        await persistence.CreateUserMessageAsync(
            new CreateUserMessageRequest(conversationId, turn.TurnIndex, 1, UserText, SeedData.ModelDeploymentId, seed.UserId, null, null), ct);
        var assistantMessageId = await persistence.StartAssistantMessageAsync(
            new StartAssistantMessageRequest(conversationId, turn.TurnId, turn.TurnIndex, 2, SeedData.AssistantName, SeedData.ModelDeploymentId, null,
                Content: string.Empty, IsStreaming: true, ExpectedExecutionId: execution), ct);
        var checkpointed = await persistence.CheckpointTurnAsync(turn.TurnId, assistantMessageId, PartialReply, null, 1, ct, execution);
        await persistence.AppendOrFinalizeAssistantMessageAsync(
            new AssistantMessageUpdateRequest(assistantMessageId, turn.TurnId, _reply, Finalize: true, ExpectedExecutionId: execution), ct);
        var terminalized = await persistence.TerminalizeTurnAsync(
            new TerminalizeTurnRequest(turn.TurnId, conversationId, turn.TurnIndex, "completed", ExecutionId: execution), ct);
        stopwatch.Stop();

        if (execution is null || !checkpointed || !terminalized)
        {
            throw new InvalidOperationException(
                $"The turn wasn't saved as the engine saves it (execution fence {(execution is null ? "missing" : "set")}, checkpoint {checkpointed}, terminalized {terminalized}).");
        }

        measurement.Add(Name, Measurement.Ms(stopwatch));
    }

    public IReadOnlyList<string> Notes(IReadOnlyDictionary<DatabaseProvider, Measurement> measured) =>
        ["Turns in the target conversation before the runs: " +
            string.Join("; ", _turnsBefore.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {kv.Value}"))];
}
