using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;

namespace RealmPortal.Cli;

public static class Program
{
    public static int Main(string[] args) =>
        Run(args, Console.Out, Console.Error, new PeClientVersionReader());

    /// <summary>Runs the CLI. Exit codes: 0 success, 1 usage error, 2 operation failed.</summary>
    /// <param name="launcher">Starts the game; defaults to a real process launcher. Tests pass a fake.</param>
    /// <param name="getVariable">Reads environment variables; defaults to the process environment.</param>
    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        IClientVersionReader versionReader,
        IGameLauncher? launcher = null,
        Func<string, string?>? getVariable = null,
        IGameProcessProbe? probe = null)
    {
        args = StripGlobalOptions(args, out var verbose);

        if (args.Length == 1 && args[0] is "--version" or "-v")
        {
            output.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;
        }

        if (args.Length == 2 && args[0] == "client-version")
        {
            try
            {
                output.WriteLine(versionReader.Read(args[1]));
                return 0;
            }
            catch (ClientVersionException ex)
            {
                CliApp.SafeWriteLine(error, $"error: {ex.Message}");
                return 2;
            }
        }

        var app = new CliApp(output, error, versionReader, launcher ?? new ProcessGameLauncher(), getVariable ?? Environment.GetEnvironmentVariable, probe: probe);
        if (args.Length >= 1 && app.IsCommand(args[0]))
            return app.Run(args[0], args[1..], verbose);

        // Common options before the command (#129): `--config-dir <dir> list` is the same as `list --config-dir <dir>`.
        var leading = 0;
        while (leading < args.Length && CliApp.IsLeadingValueOption(args[leading]))
        {
            if (leading + 1 >= args.Length || args[leading + 1].StartsWith("--", StringComparison.Ordinal))
            {
                CliApp.SafeWriteLine(error, $"error: option {args[leading]} needs a value");
                error.WriteLine(CliApp.Usage);
                return 1;
            }
            leading += 2;
        }
        if (leading > 0 && leading < args.Length && app.IsCommand(args[leading]))
            return app.Run(args[leading], [.. args[..leading], .. args[(leading + 1)..]], verbose);

        var commandIndex = Array.FindIndex(args, static a => CliApp.Commands.Contains(a));
        var optionLimit = commandIndex < 0 ? args.Length : commandIndex;
        for (var i = 0; i < optionLimit; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && !CliApp.IsKnownLeadingOption(args[i]))
            {
                CliApp.SafeWriteLine(error, $"error: unknown option {args[i]}");
                error.WriteLine(CliApp.Usage);
                return 1;
            }
        }

        output.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        output.WriteLine(CliApp.Usage);
        return args.Length == 0 ? 0 : 1;
    }

    private static string[] StripGlobalOptions(string[] args, out bool verbose)
    {
        verbose = false;
        var list = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            // `--config-dir --verbose list`: keep the token so the missing value is reported (#129).
            if (arg == "--verbose" && !(i > 0 && CliApp.IsLeadingValueOption(args[i - 1])))
            {
                verbose = true;
                continue;
            }

            list.Add(arg);
        }

        return list.ToArray();
    }
}
