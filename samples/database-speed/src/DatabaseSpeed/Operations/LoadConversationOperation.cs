using GuideAntsApi.Models.Conversations;
using GuideAntsApi.Services.Conversations.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>Opening a long conversation: what <c>GET .../conversations/{id}</c> reads.</summary>
public sealed class LoadConversationOperation(SeedData seed) : ScopedCallOperation<NotebookConversationWithMessagesDto?>
{
    public override string Name => $"load a {seed.Volumes.LongConversationMessages}-message conversation";

    public override string Description =>
        $"`IConversationQueryService.GetConversationWithMessagesAsync(id)` (`ConversationQueryService`) on a conversation of " +
        $"{seed.Volumes.LongConversationMessages / 2} turns ({seed.Volumes.LongConversationMessages} messages: a user message and an assistant reply of " +
        "about 1-2 KB per turn), in a database that also holds the bulk conversations. The single query projects messages, users, edit history, " +
        "attachments and turns; SQL Server first runs `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED`, Postgres doesn't (parity report, intentional difference).";

    protected override Task<NotebookConversationWithMessagesDto?> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IConversationQueryService>().GetConversationWithMessagesAsync(seed.LongConversationId);

    protected override string Check(NotebookConversationWithMessagesDto? result)
    {
        Expect(result is not null, "The conversation wasn't found.");
        Expect(result!.Messages.Count == seed.Volumes.LongConversationMessages,
            $"The conversation returned {result.Messages.Count} messages; expected {seed.Volumes.LongConversationMessages}.");
        return $"{result.Messages.Count} messages";
    }
}
