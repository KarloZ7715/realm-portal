using System.Diagnostics;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Finds a bash that can run the repo's packaging scripts.
/// On Windows this is Git for Windows <c>bash.exe</c>, never the WSL stub at
/// <c>%SystemRoot%\System32\bash.exe</c> (that stub writes to stdout and exits non-zero).
/// A candidate is accepted only if <c>bash -c 'echo ok'</c> exits 0 and prints <c>ok</c>.
/// </summary>
internal static class PackagingBash
{
    internal static string? Executable { get; }
    internal static string? SkipReason { get; }

    static PackagingBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Probe("bash"))
            {
                Executable = "bash";
                return;
            }

            SkipReason = "Skipped: bash is not available to run packaging scripts.";
            return;
        }

        foreach (var candidate in WindowsCandidates())
        {
            if (candidate is not null && File.Exists(candidate) && Probe(candidate))
            {
                Executable = candidate;
                return;
            }
        }

        SkipReason =
            "Skipped: Git for Windows bash was not found or could not run packaging scripts (the WSL bash stub is not used).";
    }

    private static IEnumerable<string> WindowsCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
            yield return Path.Combine(programFiles, "Git", "bin", "bash.exe");

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
            yield return Path.Combine(programFilesX86, "Git", "bin", "bash.exe");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "Programs", "Git", "bin", "bash.exe");

        foreach (var fromGit in FromGitExecPath())
            yield return fromGit;
    }

    private static IEnumerable<string> FromGitExecPath()
    {
        string execPath;
        try
        {
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--exec-path");
            using var process = Process.Start(start);
            if (process is null)
                yield break;
            execPath = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                yield break;
            }

            if (process.ExitCode != 0 || string.IsNullOrEmpty(execPath))
                yield break;
        }
        catch
        {
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(execPath, "..", "..", "..", "bin", "bash.exe"));
        yield return Path.GetFullPath(Path.Combine(execPath, "..", "bin", "bash.exe"));
        yield return Path.Combine(execPath, "bash.exe");
    }

    private static bool Probe(string bash)
    {
        try
        {
            var start = new ProcessStartInfo(bash)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("echo ok");
            using var process = Process.Start(start);
            if (process is null)
                return false;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            return process.ExitCode == 0 && output.Trim() == "ok";
        }
        catch
        {
            return false;
        }
    }
}
