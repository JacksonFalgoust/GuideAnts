using GuideAntsApi.Models;
using GuideAntsApi.Services.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>The Projects page: every project, ordered by last activity.</summary>
public sealed class ListProjectsOperation(SeedData seed) : ScopedCallOperation<IEnumerable<ProjectDto>>
{
    public override string Name => "list projects";

    public override string Description =>
        $"`IProjectService.GetProjectsAsync()` (`ProjectService`): the {seed.Volumes.Projects} projects, each joined to the latest of its " +
        $"usage events ({seed.Volumes.UsageEvents:N0} in all) for the last-activity order.";

    protected override Task<IEnumerable<ProjectDto>> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IProjectService>().GetProjectsAsync();

    protected override string Check(IEnumerable<ProjectDto> result)
    {
        var count = result.Count();
        Expect(count == seed.Volumes.Projects, $"Listed {count} projects; expected {seed.Volumes.Projects}.");
        return $"{count} projects, first {result.First().Title}";
    }
}
