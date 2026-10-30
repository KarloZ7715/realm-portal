using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Instances;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// Single-instance guard (#197). Every test uses its own temp directory as the instance scope, so nothing depends on
/// a global instance or the user's real config directory. Cross-process tests run the test assembly itself as a probe
/// (<see cref="ProbeProgram"/>).
/// </summary>
public class SingleInstanceTests
{
    private static readonly SingleInstanceOptions Fast = new()
    {
        ForwardTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public void FirstClaim_IsPrimaryAndListens()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);

        using var claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Primary, claim.Outcome);
        Assert.True(claim.IsListening);
        Assert.False(claim.ShouldExit);
        Assert.Null(claim.Detail);
        Assert.True(File.Exists(endpoint.LockPath));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.Exists(endpoint.ChannelAddress));
    }

    [Fact]
    public void SecondClaim_ForwardsTheMessageToThePrimaryHandler()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        using var second = SingleInstance.Claim(endpoint, new InstanceMessage("play", ["realm one", "ñ"]), _ => throw new InvalidOperationException("not primary"), Fast);

        Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        Assert.Equal(InstanceReply.Ok, second.Reply);
        Assert.True(second.ShouldExit);
        Assert.Equal(new InstanceMessage("play", ["realm one", "ñ"]), received.Take());
    }

    [Fact]
    public void SeveralForwards_AreAllDeliveredInOrder()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        for (var i = 0; i < 5; i++)
        {
            using var second = SingleInstance.Claim(endpoint, new InstanceMessage("focus", [i.ToString()]), _ => InstanceReply.Ok, Fast);
            Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        }

        for (var i = 0; i < 5; i++)
            Assert.Equal(i.ToString(), Assert.Single(received.Take().Args));
    }

    [Fact]
    public void AVerbThePrimaryDoesNotHandle_IsReportedAsUnsupported()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus,
            m => m.Verb == InstanceMessage.FocusVerb ? InstanceReply.Ok : InstanceReply.Unsupported, Fast);

        using var second = SingleInstance.Claim(endpoint, new InstanceMessage("later-feature"), _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        Assert.Equal(InstanceReply.Unsupported, second.Reply);
    }

    [Fact]
    public void AThrowingHandler_AnswersInvalid_AndTheListenerKeepsServing()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var calls = 0;
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ =>
            Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("boom") : InstanceReply.Ok, Fast);

        using (var first = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast))
            Assert.Equal(InstanceReply.Invalid, first.Reply);
        using var second = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(InstanceReply.Ok, second.Reply);
    }

    [Fact]
    public void Dispose_ReleasesTheLockAndRemovesTheSocket()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);
        primary.Dispose();

        if (!OperatingSystem.IsWindows())
            Assert.False(File.Exists(endpoint.ChannelAddress));
        using var next = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);
        Assert.Equal(ClaimOutcome.Primary, next.Outcome);
        Assert.True(next.IsListening);
    }

    [Fact]
    public void DifferentDirectories_AreIndependentInstances()
    {
        using var dir = new TempDirectory();
        using var a = SingleInstance.Claim(InstanceEndpoint.For(dir.Combine("a")), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);
        using var b = SingleInstance.Claim(InstanceEndpoint.For(dir.Combine("b")), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);
        using var other = SingleInstance.Claim(InstanceEndpoint.For(dir.Combine("a"), "other"), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Primary, a.Outcome);
        Assert.Equal(ClaimOutcome.Primary, b.Outcome);
        Assert.Equal(ClaimOutcome.Primary, other.Outcome);
    }

    [Fact]
    public void ALockHeldWithoutAListener_TimesOutAsPrimaryUnresponsive()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        using var foreign = new FileStream(endpoint.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var timer = Stopwatch.StartNew();

        using var claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok,
            Fast with { ForwardTimeout = TimeSpan.FromMilliseconds(600) });

        Assert.Equal(ClaimOutcome.PrimaryUnresponsive, claim.Outcome);
        Assert.False(claim.ShouldExit);
        Assert.NotNull(claim.Detail);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"took {timer.Elapsed}");
    }

    [UnixFact]
    public void APrimaryThatAcceptsButNeverAnswers_TimesOutAsPrimaryUnresponsive()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        using var foreign = new FileStream(endpoint.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var hung = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        hung.Bind(new UnixDomainSocketEndPoint(endpoint.ChannelAddress!));
        hung.Listen(1); // never accepts: connections sit in the backlog like with a stopped (SIGSTOP) process
        var timer = Stopwatch.StartNew();

        using var claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok,
            Fast with { ForwardTimeout = TimeSpan.FromMilliseconds(600) });

        Assert.Equal(ClaimOutcome.PrimaryUnresponsive, claim.Outcome);
        Assert.Contains("did not answer", claim.Detail);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"took {timer.Elapsed}");
    }

    [Fact]
    public void ALockDirectoryThatIsAFile_IsUnavailable_NotAnException()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("config");
        File.WriteAllText(path, "not a directory");

        using var claim = SingleInstance.Claim(InstanceEndpoint.For(path), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Unavailable, claim.Outcome);
        Assert.False(claim.ShouldExit);
    }

    /// <remarks>A real stale socket (left by a killed process) is covered by <see cref="AKilledPrimaryProcess_DoesNotBlockTheNextStart"/>;
    /// a socket closed in-process cannot be staged because .NET unlinks the path on dispose.</remarks>
    [UnixFact]
    public void AStaleFileAtTheSocketPathWithoutALock_IsReplacedByTheNextPrimary()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        File.WriteAllText(endpoint.ChannelAddress!, "stale");
        Assert.True(File.Exists(endpoint.ChannelAddress));

        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);
        using var second = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Primary, primary.Outcome);
        Assert.True(primary.IsListening);
        Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        Assert.Equal(InstanceMessage.Focus, received.Take());
    }

    [NonPrivilegedUnixFact]
    public void TheSocketAndTheLockFile_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
            return; // Skipped by the attribute; this guard is for the platform analyzer.
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Combine("config"));

        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        const UnixFileMode ownerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(ownerReadWrite, File.GetUnixFileMode(endpoint.ChannelAddress!));
        Assert.Equal(ownerReadWrite, File.GetUnixFileMode(endpoint.LockPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(endpoint.Directory));
    }

    [UnixFact]
    public void ALongDirectory_UsesAShortSocketUnderXdgRuntimeDir_AndStillWorks()
    {
        using var dir = new TempDirectory();
        var runtime = dir.Combine("run");
        Directory.CreateDirectory(runtime);
        var deep = dir.Combine(new string('d', 60), new string('e', 60));
        Func<string, string?> env = name => name == "XDG_RUNTIME_DIR" ? runtime : null;
        var endpoint = InstanceEndpoint.For(deep, "app", env);

        Assert.StartsWith(runtime + Path.DirectorySeparatorChar, endpoint.ChannelAddress);
        Assert.True(Encoding.UTF8.GetByteCount(endpoint.ChannelAddress!) <= InstanceEndpoint.MaxSocketPathBytes);
        Assert.NotEqual(endpoint.ChannelAddress, InstanceEndpoint.For(deep + "x", "app", env).ChannelAddress);

        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);
        using var second = SingleInstance.Claim(InstanceEndpoint.For(deep, "app", env), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.True(primary.IsListening);
        Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        Assert.Equal(InstanceMessage.Focus, received.Take());
    }

    [NonPrivilegedUnixFact]
    public void ALongDirectoryWithoutXdgRuntimeDir_UsesAPrivateTempFolder()
    {
        if (OperatingSystem.IsWindows())
            return; // Skipped by the attribute; this guard is for the platform analyzer.
        using var dir = new TempDirectory();
        var deep = dir.Combine(new string('d', 60), new string('e', 60));
        var endpoint = InstanceEndpoint.For(deep, "app", _ => null);
        var socketDir = Path.GetDirectoryName(endpoint.ChannelAddress)!;
        try
        {
            using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

            Assert.True(primary.IsListening);
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.GetDirectoryName(socketDir));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(socketDir));
        }
        finally
        {
            // The folder is shared by every long-path instance of this user; remove it only when empty.
            try
            {
                Directory.Delete(socketDir);
            }
            catch (IOException)
            {
            }
        }
    }

    [UnixFact]
    public void AFallbackFolderThatIsASymlink_IsRefused_AndThePrimaryStillStarts()
    {
        using var dir = new TempDirectory();
        var deep = dir.Combine(new string('d', 60), new string('e', 60));
        var endpoint = InstanceEndpoint.For(deep, "app", _ => null);
        var socketDir = Path.GetDirectoryName(endpoint.ChannelAddress)!;
        if (Directory.Exists(socketDir) || File.Exists(socketDir))
            return; // Another long-path instance of this user exists; the symlink case cannot be staged safely.

        Directory.CreateSymbolicLink(socketDir, dir.Path);
        try
        {
            using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

            Assert.Equal(ClaimOutcome.Primary, primary.Outcome);
            Assert.False(primary.IsListening);
            Assert.Contains("symbolic link", primary.Detail);
        }
        finally
        {
            File.Delete(socketDir);
        }
    }

    [Fact]
    public void PrimaryProcess_ReceivesTheFocusMessageFromThisProcess()
    {
        using var dir = new TempDirectory();
        using var probe = ProbeProcess.Start("instance-probe", "primary", dir.Path);
        Assert.Equal("outcome Primary listening=True", probe.WaitFor("outcome"));

        using var second = SingleInstance.Claim(InstanceEndpoint.For(dir.Path), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
        Assert.Equal(InstanceReply.Ok, second.Reply);
        Assert.Equal("message focus", probe.WaitFor("message"));
        probe.Exit();
    }

    [Fact]
    public void SecondLaunchProcess_ForwardsAndExits()
    {
        using var dir = new TempDirectory();
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(InstanceEndpoint.For(dir.Path), InstanceMessage.Focus,
            m => { received.Handle(m); return m.Verb == "unknown" ? InstanceReply.Unsupported : InstanceReply.Ok; }, Fast);

        using (var focus = ProbeProcess.Start("instance-probe", "forward", dir.Path, "focus"))
        {
            Assert.Equal("outcome Forwarded Ok", focus.WaitFor("outcome"));
            Assert.True(focus.Process.WaitForExit(TimeSpan.FromSeconds(30)));
            Assert.Equal(0, focus.Process.ExitCode);
        }
        using (var unknown = ProbeProcess.Start("instance-probe", "forward", dir.Path, "unknown"))
            Assert.Equal("outcome Forwarded Unsupported", unknown.WaitFor("outcome"));

        Assert.Equal(InstanceMessage.Focus, received.Take());
        Assert.Equal("unknown", received.Take().Verb);
    }

    [Fact]
    public void AKilledPrimaryProcess_DoesNotBlockTheNextStart()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        using (var probe = ProbeProcess.Start("instance-probe", "primary", dir.Path))
        {
            Assert.Equal("outcome Primary listening=True", probe.WaitFor("outcome"));
            probe.Kill(); // no cleanup runs: the lock file and (on Unix) the socket file stay behind
        }
        Assert.True(File.Exists(endpoint.LockPath));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.Exists(endpoint.ChannelAddress), "the killed primary should leave a stale socket file");

        var received = new BlockingQueue();
        using var next = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);
        using var forwarded = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

        Assert.Equal(ClaimOutcome.Primary, next.Outcome);
        Assert.True(next.IsListening);
        Assert.Equal(ClaimOutcome.Forwarded, forwarded.Outcome);
        Assert.Equal(InstanceMessage.Focus, received.Take());
    }

    [LinuxFact]
    public void AChildProcessOfTheKilledPrimary_DoesNotKeepTheLock()
    {
        using var dir = new TempDirectory();
        int childId;
        using (var probe = ProbeProcess.Start("instance-probe", "primary-with-child", dir.Path, "/bin/sleep"))
        {
            childId = int.Parse(probe.WaitFor("child").Split(' ')[1]);
            Assert.Equal("outcome Primary listening=True", probe.WaitFor("outcome"));
            probe.Kill();
        }

        using var child = Process.GetProcessById(childId);
        try
        {
            Assert.False(child.HasExited, "the stand-in game process should still run");

            using var next = SingleInstance.Claim(InstanceEndpoint.For(dir.Path), InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);

            Assert.Equal(ClaimOutcome.Primary, next.Outcome);
            Assert.True(next.IsListening);
        }
        finally
        {
            child.Kill();
            child.WaitForExit(TimeSpan.FromSeconds(10));
        }
    }

    public static TheoryData<string> GarbageRequests => new()
    {
        "not json\n",
        "{}\n",
        "[]\n",
        "{\"protocol\":2,\"verb\":\"focus\"}\n",
        "{\"protocol\":\"1\",\"verb\":\"focus\"}\n",
        "{\"protocol\":1,\"verb\":\"FOCUS\"}\n",
        "{\"protocol\":1,\"verb\":\"\"}\n",
        "{\"protocol\":1,\"verb\":\"focus\",\"args\":[1]}\n",
        "{\"protocol\":1,\"verb\":\"focus\",\"args\":\"x\"}\n",
        "{\"protocol\":1,\"verb\":\"focus\",\"args\":[" + string.Join(',', Enumerable.Repeat("\"a\"", 17)) + "]}\n",
        "{\"protocol\":1,\"verb\":\"" + new string('a', 33) + "\"}\n",
        "\n",
    };

    [Theory]
    [MemberData(nameof(GarbageRequests))]
    public void GarbageRequests_AreAnsweredInvalid_NeverHandled_AndTheListenerKeepsServing(string request)
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        Assert.Equal(InstanceReply.Invalid, RawExchange(endpoint, Encoding.UTF8.GetBytes(request)));
        AssertStillServes(endpoint, received);
    }

    [Fact]
    public void InvalidUtf8_IsAnsweredInvalid()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        var bytes = Encoding.UTF8.GetBytes("{\"protocol\":1,\"verb\":\"focus\",\"args\":[\"x\"]}\n");
        bytes[37] = 0xFF;
        Assert.Equal(InstanceReply.Invalid, RawExchange(endpoint, bytes));
        AssertStillServes(endpoint, received);
    }

    [Fact]
    public void AnOversizedRequest_IsRejected_AndTheListenerKeepsServing()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        // 64 KiB without a newline: the primary stops reading at 4 KiB and does not buffer the rest.
        var reply = RawExchange(endpoint, Encoding.ASCII.GetBytes(new string('x', 64 * 1024)), tolerateReset: true);

        Assert.True(reply is null or InstanceReply.Invalid, $"unexpected reply {reply}");
        AssertStillServes(endpoint, received);
    }

    [Fact]
    public void ASilentClient_IsDroppedAfterTheRequestTimeout_AndTheNextOneIsServed()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle,
            Fast with { RequestTimeout = TimeSpan.FromMilliseconds(300) });

        using var silent = RawConnect(endpoint); // connects and sends nothing
        AssertStillServes(endpoint, received);
    }

    [Fact]
    public void ConnectionsThatCloseImmediately_DoNotStopTheListener()
    {
        using var dir = new TempDirectory();
        var endpoint = InstanceEndpoint.For(dir.Path);
        var received = new BlockingQueue();
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, received.Handle, Fast);

        for (var i = 0; i < 5; i++)
            RawConnect(endpoint).Dispose();

        AssertStillServes(endpoint, received);
    }

    [Property(MaxTest = 300)]
    public bool DecodingArbitraryBytes_NeverThrows(byte[] bytes)
    {
        _ = InstanceProtocol.TryDecodeRequest(bytes);
        return Enum.IsDefined(InstanceProtocol.DecodeReply(bytes));
    }

    [Property(MaxTest = 300)]
    public Property ValidMessages_RoundTrip() =>
        Prop.ForAll(
            Gen.Elements("focus", "play", "a", "x-1", new string('z', 32)).ToArbitrary(),
            ArbMap.Default.ArbFor<NonNull<string>[]>(),
            (verb, rawArgs) =>
            {
                var args = rawArgs.Take(InstanceProtocol.MaxArgs).Select(a => a.Get.Length > 50 ? a.Get[..50] : a.Get).ToArray();
                var message = new InstanceMessage(verb, args);
                var line = InstanceProtocol.EncodeRequest(message);
                return line[^1] == (byte)'\n'
                    && Array.IndexOf(line, (byte)'\n') == line.Length - 1
                    && message.Equals(InstanceProtocol.TryDecodeRequest(line.AsSpan(0, line.Length - 1)));
            });

    [Theory]
    [InlineData("")]
    [InlineData("Focus")]
    [InlineData("1focus")]
    [InlineData("focus now")]
    [InlineData("fócus")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidVerbs_AreRejected(string verb)
    {
        Assert.Throws<ArgumentException>(() => new InstanceMessage(verb));
        Assert.Throws<ArgumentException>(() => InstanceEndpoint.For(Path.GetTempPath(), verb));
    }

    [Fact]
    public void TooManyArguments_AreRejected() =>
        Assert.Throws<ArgumentException>(() => new InstanceMessage("focus", Enumerable.Repeat("a", InstanceProtocol.MaxArgs + 1).ToArray()));

    [Fact]
    public void Replies_RoundTrip()
    {
        foreach (var reply in Enum.GetValues<InstanceReply>())
        {
            var line = InstanceProtocol.EncodeReply(reply);
            Assert.Equal(reply, InstanceProtocol.DecodeReply(line.AsSpan(0, line.Length - 1)));
        }
    }

    [WindowsFact]
    public void OnWindows_TheChannelIsAPerDirectoryNamedPipe()
    {
        using var dir = new TempDirectory();
        var a = InstanceEndpoint.For(dir.Combine("a"));

        Assert.True(a.UsesNamedPipe);
        Assert.StartsWith("RealmPortal-app-", a.ChannelAddress);
        Assert.Equal(a.ChannelAddress, InstanceEndpoint.For(dir.Combine("A")).ChannelAddress); // case-insensitive paths
        Assert.NotEqual(a.ChannelAddress, InstanceEndpoint.For(dir.Combine("b")).ChannelAddress);
    }

    private static void AssertStillServes(InstanceEndpoint endpoint, BlockingQueue received)
    {
        using var claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok, Fast);
        Assert.Equal(ClaimOutcome.Forwarded, claim.Outcome);
        Assert.Equal(InstanceReply.Ok, claim.Reply);
        Assert.Equal(InstanceMessage.Focus, received.Take());
        Assert.True(received.IsEmpty, "a rejected request reached the handler");
    }

    private static Stream RawConnect(InstanceEndpoint endpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new System.IO.Pipes.NamedPipeClientStream(".", endpoint.ChannelAddress!, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.CurrentUserOnly);
            pipe.Connect(10_000);
            return pipe;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(endpoint.ChannelAddress!));
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static InstanceReply? RawExchange(InstanceEndpoint endpoint, byte[] request, bool tolerateReset = false)
    {
        using var stream = RawConnect(endpoint);
        try
        {
            stream.Write(request);
            stream.Flush();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var line = InstanceProtocol.ReadLineAsync(stream, cts.Token).GetAwaiter().GetResult();
            return line is null ? null : InstanceProtocol.DecodeReply(line);
        }
        catch (IOException) when (tolerateReset)
        {
            return null;
        }
    }

    /// <summary>Thread-safe record of handled messages; <see cref="Handle"/> is the instance handler.</summary>
    private sealed class BlockingQueue
    {
        private readonly System.Collections.Concurrent.BlockingCollection<InstanceMessage> _items = new();

        public bool IsEmpty => _items.Count == 0;

        public InstanceReply Handle(InstanceMessage message)
        {
            _items.Add(message);
            return InstanceReply.Ok;
        }

        public InstanceMessage Take()
        {
            Assert.True(_items.TryTake(out var item, TimeSpan.FromSeconds(10)), "no message reached the handler");
            return item!;
        }
    }
}
