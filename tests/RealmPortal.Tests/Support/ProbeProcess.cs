using System.Collections.Concurrent;
using System.Diagnostics;

namespace RealmPortal.Tests.Support;

/// <summary>Runs <see cref="ProbeProgram"/> in a child process (same test assembly, same .NET host) and reads its output lines.</summary>
public sealed class ProbeProcess : IDisposable
{
    private readonly BlockingCollection<string> _lines = new();

    private ProbeProcess(Process process)
    {
        Process = process;
        process.OutputDataReceived += (_, e) =>
        {
            try
            {
                if (e.Data is null)
                    _lines.CompleteAdding();
                else
                    _lines.Add(e.Data);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Output after the end of the stream or after Dispose: nobody reads it any more.
            }
        };
        process.BeginOutputReadLine();
    }

    public Process Process { get; }

    public static ProbeProcess Start(params string[] args)
    {
        var info = new ProcessStartInfo(DotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add(typeof(ProbeProgram).Assembly.Location);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return new ProbeProcess(Process.Start(info)!);
    }

    /// <summary>Next output line, or a failed assertion after <paramref name="timeout"/> (default 30 s).</summary>
    public string ReadLine(TimeSpan? timeout = null)
    {
        Assert.True(_lines.TryTake(out var line, timeout ?? TimeSpan.FromSeconds(30)), "the probe process wrote nothing in time");
        return line!;
    }

    /// <summary>Waits for a line starting with <paramref name="prefix"/>, skipping others.</summary>
    public string WaitFor(string prefix, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            Assert.True(remaining > TimeSpan.Zero, $"the probe process did not write '{prefix}…' in time");
            var line = ReadLine(remaining);
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line;
        }
    }

    public void Exit()
    {
        Process.StandardInput.WriteLine("exit");
        Process.StandardInput.Flush();
        Assert.True(Process.WaitForExit(TimeSpan.FromSeconds(30)), "the probe process did not exit");
    }

    public void Kill()
    {
        Process.Kill(entireProcessTree: false);
        Assert.True(Process.WaitForExit(TimeSpan.FromSeconds(30)), "the probe process did not die");
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        Process.Dispose();
        _lines.Dispose();
    }

    private static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(host) && File.Exists(host))
            return host;
        var current = Environment.ProcessPath;
        if (current is not null && Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return current;
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }
        return "dotnet";
    }
}
