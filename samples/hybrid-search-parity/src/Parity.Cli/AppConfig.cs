using Microsoft.Extensions.Configuration;
using Parity.Core.Settings;

namespace Parity.Cli;

public static class AppConfig
{
    private const string DefaultEndpoint = "https://api.openai.com/v1";
    private const string DefaultModel = "text-embedding-3-small";

    /// <summary>
    /// appsettings.json in the sample root, then user secrets, then PARITY_-prefixed environment
    /// variables (for example PARITY_Profiles__postgres__ConnectionString), then <paramref name="overrides"/>.
    /// OPENAI_API_KEY (or, failing that, OPENROUTER_API_KEY, which also retargets a default endpoint/model
    /// to OpenRouter) fills OpenAI:ApiKey when nothing else set it.
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
            var read = environment ?? Environment.GetEnvironmentVariable;
            var openAiKey = read("OPENAI_API_KEY");
            var openRouterKey = read("OPENROUTER_API_KEY");
            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                settings.OpenAI.ApiKey = openAiKey;
            }
            else if (!string.IsNullOrWhiteSpace(openRouterKey))
            {
                settings.OpenAI.ApiKey = openRouterKey;
                // OpenRouter speaks the same embeddings wire shape; only retarget values nobody configured.
                if (settings.OpenAI.Endpoint == DefaultEndpoint)
                {
                    settings.OpenAI.Endpoint = "https://openrouter.ai/api/v1";
                }

                if (settings.OpenAI.Model == DefaultModel)
                {
                    settings.OpenAI.Model = "openai/text-embedding-3-small";
                }
            }
            else
            {
                settings.OpenAI.ApiKey = null;
            }
        }

        return settings;
    }
}
