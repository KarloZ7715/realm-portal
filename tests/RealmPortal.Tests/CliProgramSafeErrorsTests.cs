using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#155: Program.cs error lines escape arguments and exception messages.</summary>
public sealed class CliProgramSafeErrorsTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var dir = new TempDirectory();
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(
            args, o, e, new PeClientVersionReader(), new FakeLauncher(),
            TestBackupDir.Variables(dir), new NeverRunningGameProbe());
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out, string Err) RunWithReader(IClientVersionReader reader, params string[] args)
    {
        using var dir = new TempDirectory();
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(
            args, o, e, reader, new FakeLauncher(),
            TestBackupDir.Variables(dir), new NeverRunningGameProbe());
        return (code, o.ToString(), e.ToString());
    }

    private sealed class HostileMessageReader : IClientVersionReader
    {
        public ClientVersion Read(string clientDirectory) =>
            throw new ClientVersionException(
                ClientVersionErrorKind.ExecutableNotFound,
                clientDirectory,
                "The client directory 'evil\nUndone: restored 'FAKE'.\u001B[31m' does not exist.");
    }

    [Fact]
    public void EscapeForTerminal_EscapesNewlineAndEscape()
    {
        Assert.Equal("a\\u000Ab\\u001Bc", CliApp.EscapeForTerminal("a\nb\u001Bc"));
    }

    [Fact]
    public void ClientVersion_ExceptionMessageWithControls_StaysEscapedOnOneLine()
    {
        var result = RunWithReader(new HostileMessageReader(), "client-version", "/tmp/unused");

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.DoesNotContain('\n', result.Err.TrimEnd('\r', '\n'));
        Assert.DoesNotContain('\u001B', result.Err);
        Assert.Contains("\\u000A", result.Err);
        Assert.Contains("\\u001B", result.Err);
        Assert.StartsWith("error: ", result.Err);
        var lines = result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void ClientVersion_HostileDirectoryPath_EscapesControlsInError()
    {
        // Windows does not allow control characters in folder names.
        if (OperatingSystem.IsWindows())
            return;

        using var root = new TempDirectory();
        var hostile = Path.Combine(root.Path, "ev\u001B[31mil\nUndone: restored 'FAKE'.");
        Directory.CreateDirectory(hostile);

        var result = Run("client-version", hostile);

        Assert.Equal(2, result.Code);
        Assert.DoesNotContain('\u001B', result.Err);
        Assert.Contains("\\u001B", result.Err);
        Assert.Contains("\\u000A", result.Err);
        Assert.DoesNotContain('\n', result.Err.TrimEnd('\r', '\n'));
        var errorLines = result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(errorLines);
        Assert.DoesNotContain(errorLines, line => line.StartsWith("Undone: restored 'FAKE'", StringComparison.Ordinal));
    }

    [Fact]
    public void LeadingOptionNeedsValue_WithControlsElsewhereInArgv_ErrorStaysOneEscapedLine()
    {
        // Input carries \n and ESC in the rejected value token; the printed option name is fixed,
        // but the line still goes through SafeWriteLine and must not leak those controls.
        var result = Run("--config-dir", "--evil\n\u001B[31mfake", "list");

        Assert.Equal(1, result.Code);
        Assert.Contains("error: option --config-dir needs a value", result.Err);
        Assert.DoesNotContain('\u001B', result.Err);
        Assert.DoesNotContain("evil", result.Err);
        var first = result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('\r');
        Assert.Equal("error: option --config-dir needs a value", first);
        Assert.DoesNotContain('\n', first);
    }

    [Fact]
    public void UnknownOption_WithNewlineAndEscape_StaysEscapedOnOneLine()
    {
        var evil = "--bad\nUndone: restored 'FAKE'.\u001B[31m";
        var result = Run(evil, "list");

        Assert.Equal(1, result.Code);
        Assert.DoesNotContain('\u001B', result.Err);
        Assert.Contains("\\u000A", result.Err);
        Assert.Contains("\\u001B", result.Err);
        Assert.Contains("error: unknown option", result.Err);
        var errLines = result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = errLines[0].TrimEnd('\r');
        Assert.StartsWith("error: unknown option", first);
        Assert.DoesNotContain('\n', first);
        // Escaped payload may still contain the words; it must not form its own injected line.
        Assert.DoesNotContain(errLines, line => line.TrimEnd('\r').StartsWith("Undone: restored 'FAKE'", StringComparison.Ordinal));
    }
}
