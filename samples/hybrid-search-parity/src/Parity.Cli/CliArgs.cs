using Parity.Core;

namespace Parity.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int NotSwitchable = 1;
    public const int ConfigurationOrData = 2;
}

public sealed record CliArgs(string Command, IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string?> Options)
{
    private static readonly HashSet<string> Flags = ["--all", "--quirk-fixed"];
    private static readonly HashSet<string> Valued = ["--profile", "--scope", "--out", "--query", "--corpus-commit", "--baseline", "--candidate"];

    public static CliArgs Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            return new CliArgs("help", [], new Dictionary<string, string?>());
        }

        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var positionals = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg))
            {
                options[arg] = null;
            }
            else if (Valued.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ParityConfigurationException($"Option {arg} needs a value.");
                }

                options[arg] = args[++i];
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ParityConfigurationException($"Unknown option {arg}. Run with --help for usage.");
            }
            else
            {
                positionals.Add(arg);
            }
        }

        return new CliArgs(args[0], positionals, options);
    }

    public bool Has(string option) => Options.ContainsKey(option);

    public string? Get(string option) => Options.TryGetValue(option, out var value) ? value : null;
}
