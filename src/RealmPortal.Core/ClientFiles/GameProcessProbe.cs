using System.Diagnostics;

namespace RealmPortal.Core.ClientFiles;

/// <summary>
/// Looks for a process named like a WoW executable (<c>Wow</c>, <c>Wow-64</c>, <c>WowClassic</c>; a trailing <c>.exe</c> is ignored,
/// which covers Wine). The client folder is not compared: a game running from another folder also blocks, which errs on the safe side.
/// </summary>
public sealed class GameProcessProbe : IGameProcessProbe
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase) { "wow", "wow-64", "wowb", "wowt", "wowclassic" };

    public bool IsGameRunning(string clientRoot)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        name = name[..^4];
                    if (Names.Contains(name))
                        return true;
                }
                catch (InvalidOperationException)
                {
                    // The process exited while listing.
                }
            }
        }

        return false;
    }
}
