using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// A path-prefix lookup. Carry-over check: under the nondeterministic <c>guideants_ci</c> collation a
/// <c>LIKE 'prefix%'</c> can't use a B-tree range scan on Postgres, while SQL Server can seek
/// <c>IX_NotebookFiles_RelativePath_NotebookId</c>.
/// </summary>
/// <remarks>
/// Production runs this query inside <c>NotebookFileService</c>'s folder delete (NotebookFileService.cs:1794), which also
/// deletes files on disk, so the harness runs the same LINQ on the host-configured scoped context instead of the method.
/// </remarks>
public sealed class PathPrefixOperation(SeedData seed) : ScopedCallOperation<List<NotebookFile>>, IExplainable
{
    private readonly string _prefix = SeedData.PathPrefixFolder + "/";

    public override string Name => "path-prefix lookup (notebook files)";

    public override string Description =>
        $"`NotebookFiles.Where(f => f.NotebookId == id && f.RelativePath.StartsWith(\"{_prefix}\")).ToListAsync()`: the folder lookup of " +
        "`NotebookFileService`'s folder delete (NotebookFileService.cs:1794; the same shape as `ProjectFolderService`'s rename at :466), run as " +
        "that LINQ on the host-configured scoped context because the method also deletes files on disk. " +
        $"{seed.Volumes.NotebookFilesPerNotebook} files per notebook in 4 folders, {(long)seed.Volumes.Notebooks * seed.Volumes.NotebookFilesPerNotebook:N0} in all. " +
        "Watch item: under Postgres' nondeterministic `guideants_ci` collation `LIKE 'prefix%'` can't range-scan a B-tree, while SQL Server can seek " +
        "`IX_NotebookFiles_RelativePath_NotebookId`; the plan below shows what each database does.";

    public string ExplainTitle => $"path-prefix lookup: `WHERE NotebookId = @id AND RelativePath LIKE '{_prefix}%'` (NotebookFileService.cs:1794)";

    public IQueryable ExplainQuery(ApplicationDbContext db) => Query(db);

    protected override Task<List<NotebookFile>> CallAsync(IServiceProvider services, CancellationToken ct) =>
        Query(services.GetRequiredService<ApplicationDbContext>()).ToListAsync(ct);

    protected override string Check(List<NotebookFile> result)
    {
        var expected = (seed.Volumes.NotebookFilesPerNotebook + 3) / 4;
        Expect(result.Count == expected, $"The prefix matched {result.Count} files; expected {expected}.");
        return $"{result.Count} files";
    }

    private IQueryable<NotebookFile> Query(ApplicationDbContext db)
    {
        var notebookId = seed.PathPrefixNotebookId;
        var prefix = _prefix;
        return db.NotebookFiles.Where(f => f.NotebookId == notebookId && f.RelativePath.StartsWith(prefix));
    }
}
