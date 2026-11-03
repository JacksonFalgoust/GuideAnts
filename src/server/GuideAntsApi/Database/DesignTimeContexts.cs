using GuideAntsApi.DataModel;
using Microsoft.EntityFrameworkCore.Infrastructure;

// EF design-time tools look for contexts in the startup and target assemblies only. The Postgres migrations
// (GuideAntsApi.DataModel.Postgres) do not contain ApplicationDbContext, so the startup assembly names it here.
// The tools then use ApplicationDbContextDesignTimeFactory from the context's own assembly, for either provider.
[assembly: DbContext(typeof(ApplicationDbContext))]
