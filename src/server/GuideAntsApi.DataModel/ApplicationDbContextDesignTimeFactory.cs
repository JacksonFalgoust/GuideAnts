using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GuideAntsApi.DataModel;

public sealed class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:DefaultConnection")
            ?? "Server=localhost,1434;Initial Catalog=guideants-dev-major-refactor-20260415;Persist Security Info=False;User ID=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;ConnectRetryCount=3;ConnectRetryInterval=5;";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var provider = ResolveProvider(args);
        ApplicationDbContextOptions.Configure(optionsBuilder, provider, connectionString, new DbProviderTuning(60, true, false));

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static DatabaseProvider ResolveProvider(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--provider", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return DatabaseProviderSettings.Parse(args[i + 1]);
            }
        }

        return DatabaseProviderSettings.FromEnvironment();
    }
}
