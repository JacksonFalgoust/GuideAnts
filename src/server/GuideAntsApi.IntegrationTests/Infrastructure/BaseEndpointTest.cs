using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GuideAntsApi.DataModel;

namespace GuideAntsApi.IntegrationTests.Infrastructure;

public abstract class BaseEndpointTest : BaseIntegrationTest
{
    protected override async Task CleanDatabaseAsync()
    {
        if (SharedFactory == null) return;

        using var scope = SharedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Use set-based deletes (ExecuteDeleteAsync, no change tracking) to avoid EF cascade issues; they
        // translate on both SQL Server and Postgres. Each inner join keeps a row only when its parent chain
        // up to Projects exists, as the DELETE ... INNER JOIN statements this replaces did.
        // Delete in dependency order to avoid foreign key violations

        // First delete message edit histories (deepest level)
        await (
            from meh in dbContext.MessageEditHistories
            join ncm in dbContext.NotebookConversationMessages on meh.MessageId equals ncm.Id
            join nc in dbContext.NotebookConversations on ncm.NotebookConversationId equals nc.Id
            join n in dbContext.Notebooks on nc.NotebookId equals n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select meh).ExecuteDeleteAsync();

        // Delete conversation messages
        await (
            from ncm in dbContext.NotebookConversationMessages
            join nc in dbContext.NotebookConversations on ncm.NotebookConversationId equals nc.Id
            join n in dbContext.Notebooks on nc.NotebookId equals n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select ncm).ExecuteDeleteAsync();

        // Delete conversation turns
        await (
            from ct in dbContext.ConversationTurns
            join nc in dbContext.NotebookConversations on ct.NotebookConversationId equals nc.Id
            join n in dbContext.Notebooks on nc.NotebookId equals n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select ct).ExecuteDeleteAsync();

        // Delete file lineage events (NotebookId is nullable; the inner join skips rows without a notebook)
        await (
            from fle in dbContext.FileLineageEvents
            join n in dbContext.Notebooks on fle.NotebookId equals (Guid?)n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select fle).ExecuteDeleteAsync();

        // Delete notebook files
        await (
            from nf in dbContext.NotebookFiles
            join n in dbContext.Notebooks on nf.NotebookId equals n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select nf).ExecuteDeleteAsync();

        // Delete notebook conversations
        await (
            from nc in dbContext.NotebookConversations
            join n in dbContext.Notebooks on nc.NotebookId equals n.Id
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select nc).ExecuteDeleteAsync();


        // Delete content file versions FIRST because they have FK to ProjectFolders
        await (
            from cfv in dbContext.ContentFileVersions
            join cf in dbContext.ContentFiles on cfv.ContentFileId equals cf.Id
            join p in dbContext.Projects on cf.ProjectId equals p.Id
            select cfv).ExecuteDeleteAsync();

        // Delete content files
        await (
            from cf in dbContext.ContentFiles
            join p in dbContext.Projects on cf.ProjectId equals p.Id
            select cf).ExecuteDeleteAsync();

        // Delete project folders
        await (
            from pf in dbContext.ProjectFolders
            join p in dbContext.Projects on pf.ProjectId equals p.Id
            select pf).ExecuteDeleteAsync();

        // Delete host folder mount links and mounts
        await (
            from hfl in dbContext.HostFolderMountLinks
            join hfm in dbContext.HostFolderMounts on hfl.HostFolderMountId equals hfm.Id
            join p in dbContext.Projects on hfm.ProjectId equals p.Id
            select hfl).ExecuteDeleteAsync();

        await (
            from hfm in dbContext.HostFolderMounts
            join p in dbContext.Projects on hfm.ProjectId equals p.Id
            select hfm).ExecuteDeleteAsync();

        // Delete published guides before notebooks (FK_PublishedGuides_Notebooks_NotebookId).
        // The system-guide seeder publishes the seeded guides, so these rows reference
        // system-project notebooks and would block the notebook delete below.
        await dbContext.PublishedGuides.ExecuteDeleteAsync();

        // Delete scheduled job runs and jobs before notebooks (FK_ProjectScheduledJobs_Notebooks_NotebookId).
        await (
            from psjr in dbContext.ProjectScheduledJobRuns
            join psj in dbContext.ProjectScheduledJobs on psjr.ScheduledJobId equals psj.Id
            join p in dbContext.Projects on psj.ProjectId equals p.Id
            select psjr).ExecuteDeleteAsync();

        await (
            from psj in dbContext.ProjectScheduledJobs
            join p in dbContext.Projects on psj.ProjectId equals p.Id
            select psj).ExecuteDeleteAsync();

        // Delete notebooks
        await (
            from n in dbContext.Notebooks
            join p in dbContext.Projects on n.ProjectId equals p.Id
            select n).ExecuteDeleteAsync();

        // Delete links
        await (
            from l in dbContext.Links
            join p in dbContext.Projects on l.ProjectId equals p.Id
            select l).ExecuteDeleteAsync();

        // Delete semi-structured data
        await (
            from ssd in dbContext.SemiStructuredProjectDatas
            join p in dbContext.Projects on ssd.ProjectId equals p.Id
            select ssd).ExecuteDeleteAsync();

        // Delete projects
        await dbContext.Projects.ExecuteDeleteAsync();

        // Delete test users
        await dbContext.Users
            .Where(u => EF.Functions.Like(u.Email, "%@example.com%") || EF.Functions.Like(u.Email, "%test%"))
            .ExecuteDeleteAsync();
    }
}
