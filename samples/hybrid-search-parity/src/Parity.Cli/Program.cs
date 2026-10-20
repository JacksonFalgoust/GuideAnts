using Parity.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    return await new CliApp(Console.Out, Console.Error).RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return ExitCodes.ConfigurationOrData;
}
