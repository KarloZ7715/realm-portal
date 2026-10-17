using RealmPortal.Core;

namespace RealmPortal.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--version" or "-v")
        {
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;
        }

        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        Console.WriteLine("Usage: realm-portal [--version]");
        return args.Length == 0 ? 0 : 1;
    }
}
