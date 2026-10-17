using RealmPortal.Core;
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
        Func<string, string?>? getVariable = null)
    {
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
                error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        var app = new CliApp(output, error, versionReader, launcher ?? new ProcessGameLauncher(), getVariable ?? Environment.GetEnvironmentVariable);
        if (args.Length >= 1 && app.IsCommand(args[0]))
            return app.Run(args[0], args[1..]);

        output.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        output.WriteLine(CliApp.Usage);
        return args.Length == 0 ? 0 : 1;
    }
}
