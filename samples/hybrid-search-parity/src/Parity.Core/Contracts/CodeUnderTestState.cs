namespace Parity.Core.Contracts;

/// <summary>
/// The repository code a production profile runs (paths relative to the repository root), the last commit that changed
/// any of it, and whether it has uncommitted changes. LastCommit is "unknown" when git couldn't tell.
/// </summary>
public sealed record CodeUnderTestState(IReadOnlyList<string> Paths, string LastCommit, bool Dirty);
