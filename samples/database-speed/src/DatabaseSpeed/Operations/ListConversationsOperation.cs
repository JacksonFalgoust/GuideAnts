using GuideAntsApi.Models.Conversations;
using GuideAntsApi.Services.Conversations.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>Opening a notebook: its conversations, ordered by last activity.</summary>
public sealed class ListConversationsOperation(SeedData seed) : ScopedCallOperation<IReadOnlyList<NotebookConversationListDto>>
{
    public override string Name => "list a notebook's conversations";

    public override string Description =>
        $"`IConversationQueryService.GetListAsync(notebookId)` (`ConversationQueryService`): the notebook's {seed.Volumes.ConversationsPerNotebook} " +
        "conversations, each with its latest usage event as last activity (SQL Server first sets `READ UNCOMMITTED`).";

    protected override Task<IReadOnlyList<NotebookConversationListDto>> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IConversationQueryService>().GetListAsync(seed.ListConversationsNotebookId);

    protected override string Check(IReadOnlyList<NotebookConversationListDto> result)
    {
        Expect(result.Count == seed.Volumes.ConversationsPerNotebook, $"Listed {result.Count} conversations; expected {seed.Volumes.ConversationsPerNotebook}.");
        return $"{result.Count} conversations";
    }
}
