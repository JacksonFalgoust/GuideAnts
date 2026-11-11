using GuideAntsApi.DataModel;
using GuideAntsApi.Models.Guides;
using GuideAntsApi.Services.Guides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// The model catalog. Carry-over check: <c>IX_Models_IsActive_DisplayOrder</c> is on a nullable column, so on Postgres it
/// can't serve SQL Server's <c>ORDER BY DisplayOrder NULLS FIRST</c> directly (parity report #30, Task 8 index note).
/// </summary>
public sealed class ListModelsOperation : ScopedCallOperation<IEnumerable<ModelDto>>, IExplainable
{
    public override string Name => "list models (catalog)";

    public override string Description =>
        $"`ICatalogService.GetModelsAsync()` (`CatalogService`): the active models ordered by `DisplayOrder`, over {SeedData.CatalogModels} seeded models " +
            "(every fifth without a `DisplayOrder`, every eighth inactive; the migrations seed none). " +
        "Watch item: `IX_Models_IsActive_DisplayOrder` is on a nullable column, so Postgres can't use it for the SQL Server NULL order " +
        "(`NULLS FIRST`) and sorts instead; the plan below shows what each database does.";

    public string ExplainTitle => "list models: `WHERE IsActive ORDER BY DisplayOrder` (CatalogService.cs:27)";

    public IQueryable ExplainQuery(ApplicationDbContext db) => db.Models.Where(m => m.IsActive).OrderBy(m => m.DisplayOrder);

    protected override Task<IEnumerable<ModelDto>> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICatalogService>().GetModelsAsync();

    protected override string Check(IEnumerable<ModelDto> result)
    {
        var count = result.Count();
        Expect(count > 0, "The catalog listed no models.");
        return $"{count} active models";
    }
}
