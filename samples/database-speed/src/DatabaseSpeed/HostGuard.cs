using System.Runtime.InteropServices;

namespace DatabaseSpeed;

/// <param name="Message">What to print: the refusal, or the smoke-run warning. Null for a normal full run.</param>
public sealed record HostDecision(bool Proceed, int ExitCode, string? Message);

/// <summary>
/// Refuses a full run anywhere but x86-64. The SQL Server image is amd64-only, so on any other host it runs under
/// emulation (Rosetta/QEMU on Apple Silicon) and looks far slower than it is, which would make the comparison invalid.
/// <c>--smoke</c> bypasses the guard to prove the operations run, and its output is labelled not valid.
/// </summary>
public static class HostGuard
{
    public const int RefusedExitCode = 2;

    public static HostDecision Evaluate(Architecture architecture, bool smoke)
    {
        if (smoke)
        {
            var where = architecture == Architecture.X64
                ? "this is an x86-64 host, but the data volumes and run counts are tiny"
                : $"this host is {architecture}, so SQL Server's amd64-only image runs emulated, and the data volumes and run counts are tiny";
            return new HostDecision(true, 0,
                $"SMOKE RUN — NOT VALID FOR COMPARISON: {where}. The output only proves every operation runs on both providers.");
        }

        if (architecture == Architecture.X64)
        {
            return new HostDecision(true, 0, null);
        }

        return new HostDecision(false, RefusedExitCode,
            $"Refusing a full run on this {architecture} host: the SQL Server image is amd64-only, so here it would run emulated " +
            "and look slower than it is, which makes the comparison invalid. Run it on x86-64 Linux, for example the " +
            "`database-speed` job in CI (workflow_dispatch on .github/workflows/client-test.yml). " +
            "To check that every operation runs on this machine, use --smoke (its output is not valid for comparison).");
    }
}
