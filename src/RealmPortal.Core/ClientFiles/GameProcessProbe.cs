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
                        if (LinuxProcessBlocks($"/proc/{process.Id}", root))
                            return true;
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

    internal enum TaskState { Gone, Exited, Live, Unreadable }

    private enum TaskMatch { NoMatch, Match, Unreadable }

    /// <summary>
    /// Decides whether the process at <paramref name="procDirectory"/> (<c>/proc/&lt;pid&gt;</c>) blocks changes to
    /// <paramref name="normalizedRoot"/>: true when it runs in that folder, or when it is alive but cannot be inspected
    /// (fail-closed). A process that vanished, or whose threads are all zombie/dead, does not block. When only the
    /// thread-group leader exited (e.g. Wine's <c>ExitThread</c> on the main thread), the process lives on in its other
    /// threads, so the folder match uses a live thread's <c>/proc/&lt;pid&gt;/task/&lt;tid&gt;</c> entries.
    /// </summary>
    internal static bool LinuxProcessBlocks(string procDirectory, string normalizedRoot)
    {
        switch (ReadTaskState(procDirectory))
        {
            case TaskState.Gone:
                return false;
            case TaskState.Unreadable:
                return true;
            case TaskState.Live:
                var match = MatchTask(procDirectory, normalizedRoot);
                if (match != TaskMatch.Unreadable)
                    return match == TaskMatch.Match;
                // The leader's metadata could not be read: it may have exited meanwhile.
                switch (ReadTaskState(procDirectory))
                {
                    case TaskState.Gone:
                        return false;
                    case TaskState.Exited:
                        break;
                    default:
                        return true;
                }
                break;
        }

        // The leader is a zombie or dead; the process is alive while any of its threads is.
        string[] tasks;
        try
        {
            tasks = Directory.GetDirectories(Path.Combine(procDirectory, "task"));
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        foreach (var task in tasks)
        {
            var state = ReadTaskState(task);
            if (state is TaskState.Gone or TaskState.Exited)
                continue;
            if (state == TaskState.Unreadable)
                return true;

            var match = MatchTask(task, normalizedRoot);
            if (match == TaskMatch.Match)
                return true;
            if (match == TaskMatch.Unreadable && ReadTaskState(task) is not (TaskState.Gone or TaskState.Exited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads <c>stat</c> under a <c>/proc/&lt;pid&gt;</c> or <c>/proc/&lt;pid&gt;/task/&lt;tid&gt;</c> directory. A missing
    /// directory or file (ENOENT), or an I/O error once the directory is gone (ESRCH), means <see cref="TaskState.Gone"/>;
    /// any other failure, such as permission denied (EACCES), is <see cref="TaskState.Unreadable"/>.
    /// </summary>
    internal static TaskState ReadTaskState(string taskDirectory)
    {
        try
        {
            return StatShowsExited(File.ReadAllText(Path.Combine(taskDirectory, "stat"))) ? TaskState.Exited : TaskState.Live;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return TaskState.Gone;
        }
        catch (UnauthorizedAccessException)
        {
            return TaskState.Unreadable;
        }
        catch (IOException)
        {
            return Directory.Exists(taskDirectory) ? TaskState.Unreadable : TaskState.Gone;
        }
    }

    private static TaskMatch MatchTask(string taskDirectory, string normalizedRoot)
    {
        try
        {
            var cwd = NormalizePath(new DirectoryInfo(Path.Combine(taskDirectory, "cwd")).ResolveLinkTarget(true)?.FullName
                ?? throw new IOException("Process working directory is unavailable."));
            if (PathEquals(cwd, normalizedRoot))
                return TaskMatch.Match;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return TaskMatch.Unreadable;
        }

        try
        {
            var cmdline = File.ReadAllText(Path.Combine(taskDirectory, "cmdline"));
            return CommandLineMatches(cmdline, normalizedRoot) ? TaskMatch.Match : TaskMatch.NoMatch;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TaskMatch.Unreadable;
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
