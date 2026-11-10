using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Services.Components;
using GuideAntsApi.Services.Components.Sync;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Production code that recognizes a constraint violation by finding the constraint's name in an exception message
/// (parity report #56). SQL Server's messages name the index or foreign key (2601/2627: "... with unique index
/// 'IX_...'", 547: "... conflicted with the REFERENCE constraint "FK_..."); Postgres's <c>PostgresException.Message</c>
/// is "&lt;SQLSTATE&gt;: &lt;message text&gt;", and the text names it too (23505: "... violates unique constraint
/// "IX_..."", 23503: "... violates foreign key constraint "FK_..."..."). Each test causes the violation the way the
/// production path does and applies the production check: the reconciler's and the sync service's methods, and a copy
/// of the endpoint's inline filter. The tests run on both providers against the suite's database (no web host).
/// </summary>
/// <remarks>
/// The names are kept whole on Postgres: they are all shorter than the 63-byte identifier limit (#8).
/// </remarks>
[TestClass]
public sealed class ConstraintMessageBehaviorTests
{
    /// <summary>Unused by every job handler, so no background processor claims these rows.</summary>
    private const string TestJobType = "ConstraintMessageBehaviorTest";

    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly List<Guid> _projectIds = [];

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
        await db.JobQueue.Where(j => j.JobType == TestJobType).ExecuteDeleteAsync();
        // Versions first: their OriginNotebookFileId would block the notebook's cascade into NotebookFiles.
        await db.ContentFileVersions.Where(v => _projectIds.Contains(v.ContentFile.ProjectId)).ExecuteDeleteAsync();
        await db.ContentFiles.Where(f => _projectIds.Contains(f.ProjectId)).ExecuteDeleteAsync();
        await db.Notebooks.Where(n => _projectIds.Contains(n.ProjectId)).ExecuteDeleteAsync();
        await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    /// <summary>
    /// <c>NotebookFileReconciler.IsDuplicateNotebookFileConstraint</c>: two syncs that register the same file at once.
    /// The second insert violates <c>IX_NotebookFiles_RelativePath_NotebookId</c> and <c>IX_NotebookFiles_DocumentId</c>
    /// (the document id hashes the notebook and the path); which one a provider reports may differ, and the check
    /// accepts either.
    /// </summary>
    [TestMethod]
    public async Task Duplicate_notebook_file_registration_is_recognized_by_its_unique_index_name()
    {
        var notebookId = await SeedNotebookAsync();
        await using (var db = CreateContext())
        {
            db.NotebookFiles.Add(NewNotebookFile(notebookId, "notes/same.md"));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            db.NotebookFiles.Add(NewNotebookFile(notebookId, "notes/same.md"));
            var save = () => db.SaveChangesAsync();

            var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
            NotebookFileReconciler.IsDuplicateNotebookFileConstraint(exception).Should().BeTrue(exception.InnerException?.Message);
        }
    }

    /// <summary>
    /// The DELETE <c>/files/{fileId}</c> endpoint (<c>NotebookEndpoints.cs</c>) answers 409
    /// <c>NotebookFile.DeleteReferenced</c> when the delete fails on
    /// <c>FK_ContentFileVersions_NotebookFiles_OriginNotebookFileId</c>: a project file version was published from the
    /// file after the service's own reference check. The foreign key is NO ACTION and not deferred on Postgres, so the
    /// delete statement itself fails and <c>SaveChanges</c> wraps the error in a <c>DbUpdateException</c>.
    /// </summary>
    [TestMethod]
    public async Task Deleting_a_notebook_file_a_project_version_came_from_is_recognized_by_its_foreign_key_name()
    {
        var notebookId = await SeedNotebookAsync();
        var projectId = _projectIds[^1];
        var notebookFile = NewNotebookFile(notebookId, "published.md");
        var contentFile = new ContentFile
        {
            FileName = "published.md",
            Path = "published.md",
            RelativePath = "published.md",
            FileSize = 1,
            ContentType = "text/markdown",
            ProjectId = projectId
        };
        contentFile.GenerateDocumentId();
        await using (var db = CreateContext())
        {
            db.NotebookFiles.Add(notebookFile);
            db.ContentFiles.Add(contentFile);
            db.ContentFileVersions.Add(new ContentFileVersion
            {
                ContentFileId = contentFile.Id,
                VersionNumber = 1,
                FileName = contentFile.FileName,
                Path = contentFile.Path,
                RelativePath = contentFile.RelativePath,
                FileSize = contentFile.FileSize,
                ContentType = contentFile.ContentType,
                FromNotebook = true,
                OriginNotebookId = notebookId,
                OriginNotebookFileId = notebookFile.Id
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            db.NotebookFiles.Remove(await db.NotebookFiles.SingleAsync(f => f.Id == notebookFile.Id));
            var save = () => db.SaveChangesAsync();

            var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
            (exception.InnerException?.Message?.Contains("FK_ContentFileVersions_NotebookFiles_OriginNotebookFileId") == true)
                .Should().BeTrue(exception.InnerException?.Message);
        }

        await using (var verify = CreateContext())
        {
            (await verify.NotebookFiles.AnyAsync(f => f.Id == notebookFile.Id)).Should().BeTrue();
        }
    }

    /// <summary>
    /// <c>NotebookFileSyncService.IsCorrelationDedupViolation</c>: a second pending reconcile job for the same
    /// notebook violates the filtered unique index <c>IX_JobQueue_CorrelationDedup</c> (correlation id, job type and
    /// status, for pending and processing jobs), and the service treats it as "already queued".
    /// </summary>
    [TestMethod]
    public async Task Duplicate_pending_job_is_recognized_by_its_unique_index_name()
    {
        var correlationId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.JobQueue.Add(NewPendingJob(correlationId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            db.JobQueue.Add(NewPendingJob(correlationId));
            var save = () => db.SaveChangesAsync();

            var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
            NotebookFileSyncService.IsCorrelationDedupViolation(exception).Should().BeTrue(exception.InnerException?.Message);
        }

        await using (var verify = CreateContext())
        {
            (await verify.JobQueue.CountAsync(j => j.CorrelationId == correlationId)).Should().Be(1);
        }
    }

    private ApplicationDbContext CreateContext() => new(_options);

    private async Task<Guid> SeedNotebookAsync()
    {
        var project = new Project { Title = "Constraint message project", Slug = $"constraint-{Guid.NewGuid():N}" };
        _projectIds.Add(project.Id);
        var notebook = new Notebook { Title = "Constraint message notebook", Slug = $"constraint-{Guid.NewGuid():N}", ProjectId = project.Id };

        await using var db = CreateContext();
        db.Projects.Add(project);
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        return notebook.Id;
    }

    private static NotebookFile NewNotebookFile(Guid notebookId, string relativePath)
    {
        var file = new NotebookFile
        {
            NotebookId = notebookId,
            RelativePath = relativePath,
            FileSize = 1,
            LastModifiedUtc = DateTime.UtcNow,
            FileHash = "hash"
        };
        file.GenerateDocumentId(notebookId);
        return file;
    }

    private static JobQueue NewPendingJob(Guid correlationId) => new()
    {
        JobType = TestJobType,
        PayloadJson = "{}",
        Status = JobStatus.Pending,
        CorrelationId = correlationId
    };
}
