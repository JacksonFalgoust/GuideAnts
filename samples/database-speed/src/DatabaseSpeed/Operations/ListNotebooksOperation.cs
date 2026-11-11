using GuideAntsApi.Models;
using GuideAntsApi.Services.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>Opening a project: its notebooks with their last activity (the notebook listing).</summary>
public sealed class ListNotebooksOperation(SeedData seed) : ScopedCallOperation<ProjectDetailsDto?>
{
    public override string Name => "list a project's notebooks";

    public override string Description =>
        $"`IProjectService.GetProjectDetailsAsync(projectId)` (`ProjectService`), what the project page loads: the project with its " +
        $"{seed.Volumes.NotebooksPerProject} notebooks, content files, links and data in one query, then each notebook's last activity from the " +
        "usage events (SQL Server wraps that read in `READ UNCOMMITTED`, Postgres doesn't).";

    protected override Task<ProjectDetailsDto?> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IProjectService>().GetProjectDetailsAsync(seed.ListNotebooksProjectId);

    protected override string Check(ProjectDetailsDto? result)
    {
        Expect(result is not null, "The project wasn't found.");
        var count = result!.Notebooks.Count();
        Expect(count == seed.Volumes.NotebooksPerProject, $"Listed {count} notebooks; expected {seed.Volumes.NotebooksPerProject}.");
        return $"{count} notebooks";
    }
}
