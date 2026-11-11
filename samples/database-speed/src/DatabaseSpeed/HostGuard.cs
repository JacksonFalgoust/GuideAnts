using System.Runtime.InteropServices;

namespace DatabaseSpeed;

/// <param name="Message">What to print: the refusal, or the smoke-run warning. Null for a normal full run.</param>
/// <param name="SqlServerEmulated">The Docker daemon isn't x86-64, so the amd64-only SQL Server image runs emulated.</param>
public sealed record HostDecision(bool Proceed, int ExitCode, string? Message, bool SqlServerEmulated);

/// <summary>
/// Refuses a full run unless both the harness and the Docker daemon are x86-64. The SQL Server image is amd64-only, so
/// on any other daemon it runs under emulation (Rosetta/QEMU on Apple Silicon) and looks far slower than it is, which
/// makes the comparison invalid; a non-x86-64 harness would also time the client side on different hardware than CI.
/// <c>--smoke</c> bypasses the guard to prove the operations run, and its output is labelled not valid.
/// </summary>
public static class HostGuard
{
    public const int RefusedExitCode = 2;

    /// <param name="processArchitecture">The harness's architecture (<c>RuntimeInformation.OSArchitecture</c>).</param>
    /// <param name="dockerArchitecture">The Docker daemon's architecture as Docker reports it (<c>x86_64</c>, <c>aarch64</c>), or null if unknown.</param>
    public static HostDecision Evaluate(Architecture processArchitecture, string? dockerArchitecture, bool smoke)
    {
        var emulated = !IsX86_64(dockerArchitecture);
        if (smoke)
        {
            var why = emulated
                ? $"the Docker daemon is {dockerArchitecture ?? "of unknown architecture"}, so SQL Server's amd64-only image runs emulated, and the data volumes and run counts are tiny"
                : "the data volumes and run counts are tiny";
            return new HostDecision(true, 0,
                $"SMOKE RUN — NOT VALID FOR COMPARISON: {why}. The output only proves every operation runs on both providers.", emulated);
        }

        const string advice =
            "Run it on x86-64 Linux, for example the `database-speed` job in CI (workflow_dispatch on .github/workflows/client-test.yml). " +
            "To check that every operation runs on this machine, use --smoke (its output is not valid for comparison).";
        if (processArchitecture != Architecture.X64)
        {
            return new HostDecision(false, RefusedExitCode,
                $"Refusing a full run on this {processArchitecture} host: the comparison needs an x86-64 harness and Docker daemon, because " +
                $"the SQL Server image is amd64-only and would run emulated, looking slower than it is. {advice}", emulated);
        }

        if (dockerArchitecture is null)
        {
            return new HostDecision(false, RefusedExitCode,
                $"Refusing a full run: couldn't read the Docker daemon's architecture, so it can't be confirmed as x86-64 (SQL Server would run emulated otherwise). {advice}", emulated);
        }

        if (emulated)
        {
            return new HostDecision(false, RefusedExitCode,
                $"Refusing a full run: the Docker daemon is {dockerArchitecture}, not x86-64, so the amd64-only SQL Server image would run emulated " +
                $"and look slower than it is. {advice}", emulated);
        }

        return new HostDecision(true, 0, null, false);
    }

    public static bool IsX86_64(string? dockerArchitecture) =>
        dockerArchitecture is not null
        && (dockerArchitecture.Equals("x86_64", StringComparison.OrdinalIgnoreCase) || dockerArchitecture.Equals("amd64", StringComparison.OrdinalIgnoreCase));
}
