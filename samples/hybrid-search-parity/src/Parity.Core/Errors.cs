namespace Parity.Core;

/// <summary>An error the CLI reports as a one-line message with exit code 2, rather than a stack trace.</summary>
public abstract class ParityException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ParityConfigurationException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class DataFileException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class EngineUnavailableException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class NotSeededException(string message, Exception? inner = null) : ParityException(message, inner);
