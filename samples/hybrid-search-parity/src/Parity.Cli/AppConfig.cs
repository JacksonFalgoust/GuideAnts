using Microsoft.Extensions.Configuration;
using Parity.Core.Settings;

namespace Parity.Cli;

public static class AppConfig
{
    /// <summary>
    /// appsettings.json in the sample root, then user secrets, then PARITY_-prefixed environment
    /// variables (for example PARITY_Profiles__postgres__ConnectionString), then <paramref name="overrides"/>.
    /// OPENAI_API_KEY fills OpenAI:ApiKey when nothing else set it.
    /// </summary>
    public static ParitySettings Load(string sampleRoot, IDictionary<string, string?>? overrides = null, Func<string, string?>? environment = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(sampleRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets(typeof(AppConfig).Assembly, optional: true)
            .AddEnvironmentVariables(prefix: "PARITY_");
        if (overrides is not null)
        {
            builder.AddInMemoryCollection(overrides);
        }

        var settings = builder.Build().Get<ParitySettings>() ?? new ParitySettings();
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            settings.OpenAI.ApiKey = (environment ?? Environment.GetEnvironmentVariable)("OPENAI_API_KEY");
        }

        return settings;
    }
}
