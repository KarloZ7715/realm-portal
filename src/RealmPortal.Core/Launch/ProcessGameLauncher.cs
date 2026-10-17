using System.Diagnostics;

namespace RealmPortal.Core.Launch;

/// <summary>Launches the client as a detached child process.</summary>
public sealed class ProcessGameLauncher : IGameLauncher
{
    public void Launch(string executablePath, string workingDirectory, string? wrapper)
    {
        var info = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(wrapper) ? executablePath : wrapper,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        if (!string.IsNullOrWhiteSpace(wrapper))
            info.ArgumentList.Add(executablePath);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start '{info.FileName}'.");
    }
}
