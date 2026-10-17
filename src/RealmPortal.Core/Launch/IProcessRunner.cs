using System.Diagnostics;

namespace RealmPortal.Core.Launch;

/// <summary>Starts a process and returns immediately. Faked in tests so that no real game is ever launched.</summary>
public interface IProcessRunner
{
    void Start(ProcessLaunch launch);
}

/// <summary>Starts the process with <see cref="Process"/> (no shell, detached from the caller's lifetime).</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public void Start(ProcessLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var info = new ProcessStartInfo
        {
            FileName = launch.FileName,
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in launch.Arguments)
            info.ArgumentList.Add(argument);
        foreach (var (name, value) in launch.Environment)
        {
            if (value is null)
                info.Environment.Remove(name);
            else
                info.Environment[name] = value;
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start '{info.FileName}'.");
    }
}
