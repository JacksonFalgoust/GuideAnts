using GuideAntsApi.DataModel;
using GuideAntsApi.Models.Guides;
using GuideAntsApi.Services.Guides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// The tool catalog. Carry-over check: <c>IX_Tools_Category_IsActive_DisplayOrder</c> has nullable columns, so on
/// Postgres it can't serve the SQL Server <c>NULLS FIRST</c> order directly.
/// </summary>
public sealed class ListToolsOperation : ScopedCallOperation<IEnumerable<ToolDto>>, IExplainable
{
    public override string Name => "list tools (catalog)";

    public override string Description =>
        "`ICatalogService.GetToolsAsync()` (`CatalogService`): the active tools ordered by `Category`, then `DisplayOrder`, over the rows the " +
        "migrations seed. Watch item: `IX_Tools_Category_IsActive_DisplayOrder` has nullable columns (and its column order already keeps it " +
        "from giving this order directly); the plan below shows what each database does.";

    public string ExplainTitle => "list tools: `WHERE IsActive ORDER BY Category, DisplayOrder` (CatalogService.cs:228)";

    public IQueryable ExplainQuery(ApplicationDbContext db) => db.Tools.Where(t => t.IsActive).OrderBy(t => t.Category).ThenBy(t => t.DisplayOrder);

    protected override Task<IEnumerable<ToolDto>> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICatalogService>().GetToolsAsync();

    protected override string Check(IEnumerable<ToolDto> result)
    {
        var count = result.Count();
        Expect(count > 0, "The catalog listed no tools.");
        return $"{count} active tools";
    }
}
