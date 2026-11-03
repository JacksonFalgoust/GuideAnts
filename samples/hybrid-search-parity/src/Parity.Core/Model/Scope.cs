namespace Parity.Core.Model;

/// <summary>Production's search filter: IndexName (the project ID as a string) plus an optional notebook.</summary>
public sealed record Scope(string IndexName, Guid? NotebookId);

/// <summary>A scope with the label used in reports and known-differences.json (P1, P2, P3, P1/N1).</summary>
public sealed record NamedScope(string Label, Scope Scope);
