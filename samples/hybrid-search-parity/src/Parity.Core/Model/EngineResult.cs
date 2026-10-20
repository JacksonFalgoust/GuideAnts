using System.Diagnostics.CodeAnalysis;

namespace Parity.Core.Model;

/// <summary>A database-reported query failure (engine error number or SQLSTATE plus message).</summary>
public sealed record EngineError(string Code, string Message);

/// <summary>
/// Either a value or the error a database returned for that query. Production swallows these
/// errors and returns an empty list; the stores surface them so "failed" never looks like "no results".
/// </summary>
public sealed class EngineResult<T>
{
    private EngineResult(T? value, EngineError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public EngineError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static EngineResult<T> Ok(T value) => new(value, null);

    public static EngineResult<T> Fail(EngineError error) => new(default, error);

    public T Unwrap(string operation) =>
        IsSuccess ? Value : throw new InvalidOperationException($"{operation} failed: [{Error.Code}] {Error.Message}");
}
