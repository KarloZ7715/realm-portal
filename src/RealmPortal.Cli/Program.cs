using RealmPortal.Core;
using RealmPortal.Core.Clients;

namespace RealmPortal.Cli;

public static class Program
{
    public static int Main(string[] args) =>
        Run(args, Console.Out, Console.Error, new PeClientVersionReader());

    /// <summary>Runs the CLI. Exit codes: 0 success, 1 usage error, 2 operation failed.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, IClientVersionReader versionReader)
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

        output.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        output.WriteLine("Usage:");
        output.WriteLine("  realm-portal --version");
        output.WriteLine("  realm-portal client-version <client-directory>");
        return args.Length == 0 ? 0 : 1;
    }
}
