using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace GuideAntsApi.Services.Migrations;

/// <summary>
/// The context the storage migration runners (<see cref="NamedStorageMigrationRunner"/>,
/// <see cref="AsciiSlugNormalizationRunner"/>) open for themselves. They run from a command-line switch, outside the
/// host's DI container, so they build it from the configured provider and connection string.
/// </summary>
/// <remarks>
/// The tuning is the one the runners always used on SQL Server: a 120 s command timeout, vector search, EF's default
/// query splitting and no retrying execution strategy. The last one matters: the runners call
/// <c>BeginTransactionAsync</c> directly, which a retrying strategy rejects on either provider.
/// </remarks>
internal static class StorageMigrationDbContext
{
    internal static readonly DbProviderTuning Tuning = new(
        CommandTimeoutSeconds: 120,
        UseVectorSearch: true,
        ExplicitSingleQuery: false,
        RetryOnFailure: false);

    internal static DbContextOptions<ApplicationDbContext> CreateOptions(DatabaseProvider provider, string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(optionsBuilder, provider, connectionString, Tuning);
        return optionsBuilder.Options;
    }

    internal static ApplicationDbContext Create(DatabaseProvider provider, string connectionString)
        => new(CreateOptions(provider, connectionString));
}
