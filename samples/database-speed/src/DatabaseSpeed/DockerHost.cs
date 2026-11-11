using DotNet.Testcontainers.Configurations;

namespace DatabaseSpeed;

/// <summary>The Docker daemon the containers will run on, asked through Testcontainers' own endpoint.</summary>
public static class DockerHost
{
    /// <returns>The daemon's architecture as Docker reports it (<c>x86_64</c>, <c>aarch64</c>), or null if it can't be read.</returns>
    public static async Task<string?> ArchitectureAsync(CancellationToken ct)
    {
        try
        {
            using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration().CreateClient();
            var info = await client.System.GetSystemInfoAsync(ct);
            return string.IsNullOrWhiteSpace(info.Architecture) ? null : info.Architecture;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Couldn't read the Docker daemon's architecture: {ex.Message}");
            return null;
        }
    }
}
