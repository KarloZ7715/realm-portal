using System.Diagnostics;

namespace RealmPortal.Core.ClientFiles;

/// <summary>Looks for a WoW process, or a Wine launcher, using the specified client directory.</summary>
public sealed class GameProcessProbe : IGameProcessProbe
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "wow", "wow-64", "wowb", "wowt", "wowclassic", "wine", "wine64",
        "wine-preloader", "wine64-preloader", "start",
    };

    public bool IsGameRunning(string clientRoot)
    {
        var root = NormalizePath(clientRoot);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        name = name[..^4];
                    if (!Names.Contains(name))
                        continue;

                    if (OperatingSystem.IsLinux())
                    {
                        var proc = $"/proc/{process.Id}";
                        if (HasExitedOnLinux(proc))
                            continue;
                        try
                        {
                            var cwd = NormalizePath(new DirectoryInfo(Path.Combine(proc, "cwd")).ResolveLinkTarget(true)?.FullName
                                ?? throw new IOException("Process working directory is unavailable."));
                            if (PathEquals(cwd, root))
                                return true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                        {
                            // A process that exited meanwhile is not running; otherwise keep the conservative guard.
                            if (HasExitedOnLinux(proc))
                                continue;
                            return true;
                        }

                        try
                        {
                            var cmdline = File.ReadAllText(Path.Combine(proc, "cmdline"));
                            if (CommandLineMatches(cmdline, root))
                                return true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            if (HasExitedOnLinux(proc))
                                continue;
                            return true;
                        }
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var executable = process.MainModule?.FileName;
                            if (executable is not null && PathEquals(Path.GetDirectoryName(executable)!, root))
                                return true;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                        {
                            if (HasExited(process))
                                continue;
                            return true;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while listing.
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="procDirectory"/> (<c>/proc/&lt;pid&gt;</c>) is gone or the process is a zombie
    /// (<c>Z</c>) or dead (<c>X</c>). Such a process has no readable cwd but is not running, so it must not
    /// trip the conservative guard.
    /// </summary>
    private static bool HasExitedOnLinux(string procDirectory)
    {
        try
        {
            return StatShowsExited(File.ReadAllText(Path.Combine(procDirectory, "stat")));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads the state field of <c>/proc/&lt;pid&gt;/stat</c>; the name in parentheses may contain spaces or ')'.</summary>
    internal static bool StatShowsExited(string stat)
    {
        var end = stat.LastIndexOf(')');
        if (end < 0 || end + 2 >= stat.Length)
            return false;
        return stat[end + 2] is 'Z' or 'X' or 'x';
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool CommandLineMatches(string commandLine, string normalizedRoot)
    {
        var path = commandLine.Replace('\0', ' ').Replace('\\', '/');
        var zDrivePath = normalizedRoot.StartsWith('/') ? $"Z:{normalizedRoot}" : normalizedRoot;
        return ContainsPath(path, normalizedRoot, PathComparison) || ContainsPath(path, zDrivePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPath(string text, string root, StringComparison comparison)
    {
        var start = 0;
        while ((start = text.IndexOf(root, start, comparison)) >= 0)
        {
            var beforeIsBoundary = start == 0 || char.IsWhiteSpace(text[start - 1]) || text[start - 1] is '"' or '\'';
            var end = start + root.Length;
            var afterIsBoundary = end == text.Length || text[end] == '/' || char.IsWhiteSpace(text[end]) || text[end] is '"' or '\'';
            if (beforeIsBoundary && afterIsBoundary)
                return true;
            start = end;
        }
        return false;
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var target = new DirectoryInfo(full).ResolveLinkTarget(true);
            if (target is not null)
                full = Path.GetFullPath(target.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Use the normalized lexical path when the directory cannot be resolved.
        }
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
