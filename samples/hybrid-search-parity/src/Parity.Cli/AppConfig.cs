using Microsoft.Extensions.Configuration;
using Parity.Core;
using Parity.Core.Settings;

namespace Parity.Cli;

public static class AppConfig
{
    private const string DefaultEndpoint = "https://api.openai.com/v1";
    private const string DefaultModel = "text-embedding-3-small";

    /// <summary>
    /// appsettings.json in the sample root, then user secrets, then PARITY_-prefixed environment
    /// variables (for example PARITY_Profiles__postgres__ConnectionString), then <paramref name="overrides"/>.
    /// Pass <c>includeLocalOverrides: false</c> to skip user secrets and PARITY_ variables (hermetic tests).
    /// OPENAI_API_KEY (or, failing that, OPENROUTER_API_KEY, which also retargets a default endpoint/model
    /// to OpenRouter) fills OpenAI:ApiKey when nothing else set it, but only for the endpoint that key belongs to.
    /// An https endpoint is required whenever a key is present.
    /// </summary>
    public static ParitySettings Load(string sampleRoot, IDictionary<string, string?>? overrides = null, Func<string, string?>? environment = null, bool includeLocalOverrides = true)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(sampleRoot)
            .AddJsonFile("appsettings.json", optional: false);
        if (includeLocalOverrides)
        {
            builder.AddUserSecrets(typeof(AppConfig).Assembly, optional: true)
                .AddEnvironmentVariables(prefix: "PARITY_");
        }

        if (overrides is not null)
        {
            builder.AddInMemoryCollection(overrides);
        }

        var settings = builder.Build().Get<ParitySettings>() ?? new ParitySettings();
        var read = environment ?? Environment.GetEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            ResolveKeyFromEnvironment(settings.OpenAI, read("OPENAI_API_KEY"), read("OPENROUTER_API_KEY"));
        }

        if (!string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey) && !IsHttps(settings.OpenAI.Endpoint))
        {
            throw new ParityConfigurationException(
                $"OpenAI:Endpoint '{settings.OpenAI.Endpoint}' must be an https URL, because the embeddings API key is sent to it.");
        }

        return settings;
    }

    /// <summary>
    /// Pairs each environment key with its own endpoint: OPENAI_API_KEY only goes to api.openai.com, OPENROUTER_API_KEY
    /// only to openrouter.ai. An endpoint nobody configured (the default) follows whichever key is set; an endpoint
    /// somebody configured is never sent a key that belongs to another service.
    /// </summary>
    private static void ResolveKeyFromEnvironment(OpenAiSettings openAi, string? openAiKey, string? openRouterKey)
    {
        var hasOpenAi = !string.IsNullOrWhiteSpace(openAiKey);
        var hasOpenRouter = !string.IsNullOrWhiteSpace(openRouterKey);
        if (!hasOpenAi && !hasOpenRouter)
        {
            openAi.ApiKey = null;
            return;
        }

        var host = HostOf(openAi.Endpoint);
        if (openAi.Endpoint == DefaultEndpoint || host == "api.openai.com")
        {
            if (hasOpenAi)
            {
                openAi.ApiKey = openAiKey;
            }
            else if (openAi.Endpoint == DefaultEndpoint)
            {
                // OpenRouter speaks the same embeddings wire shape; only retarget values nobody configured.
                UseOpenRouter(openAi, openRouterKey!);
            }
            else
            {
                throw KeyEndpointMismatch("OPENROUTER_API_KEY", host);
            }
        }
        else if (host == "openrouter.ai")
        {
            if (!hasOpenRouter)
            {
                throw KeyEndpointMismatch("OPENAI_API_KEY", host);
            }

            UseOpenRouter(openAi, openRouterKey!);
        }
        else
        {
            throw KeyEndpointMismatch(hasOpenAi ? "OPENAI_API_KEY" : "OPENROUTER_API_KEY", host);
        }
    }

    private static void UseOpenRouter(OpenAiSettings openAi, string key)
    {
        openAi.ApiKey = key;
        if (openAi.Endpoint == DefaultEndpoint)
        {
            openAi.Endpoint = "https://openrouter.ai/api/v1";
        }

        if (openAi.Model == DefaultModel)
        {
            openAi.Model = "openai/text-embedding-3-small";
        }
    }

    private static ParityConfigurationException KeyEndpointMismatch(string variable, string host) =>
        new($"{variable} would be sent to '{(host.Length == 0 ? "an invalid endpoint" : host)}', which isn't the service that key belongs to. " +
            "Set OpenAI:ApiKey in user secrets (dotnet user-secrets set OpenAI:ApiKey <key> --project src/Parity.Cli) for that endpoint, or unset OpenAI:Endpoint.");

    private static string HostOf(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : string.Empty;

    private static bool IsHttps(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
