using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RealmPortal.App.Network;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;
using RealmPortal.Core.Updates;
using RealmPortal.Core.Updates.Signing;
using RealmPortal.Tests.Support;
using TestKey = RealmPortal.App.Tests.SignedUpdateEndToEndTests.TestKey;

namespace RealmPortal.App.Tests;

/// <summary>
/// End to end for #206 over real sockets: a tiny HTTP server on 127.0.0.1 plays GitHub (release JSON, packages,
/// SHA256SUMS and SHA256SUMS.minisig), and the real feed, checker and downloader install from it for each packaged
/// kind. The updater still builds and checks the github.com HTTPS URLs; only the test's HttpClient handler sends
/// them to the loopback server. Production has no configurable feed. The release key is a throwaway test key.
/// </summary>
public class LocalFeedInstallEndToEndTests
{
    private const string Running = "1.2.0";
    private const string Tag = "v1.2.1";

    private static readonly byte[] Elf = [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, (byte)'A', (byte)'I', 2];

    private static string Download(string name) =>
        $"https://github.com/KarloZ7715/realm-portal/releases/download/{Tag}/{name}";

    /// <summary>A one-request-per-connection HTTP/1.1 server on 127.0.0.1. Paths are "/host/path".</summary>
    private sealed class LoopbackFeed : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public LoopbackFeed()
        {
            _listener.Start();
            _loop = Task.Run(AcceptLoop);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public List<string> Requested { get; } = [];

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                var path = requestLine?.Split(' ') is [_, var p, ..] ? p : "/";
                var url = "https://" + path.TrimStart('/');
                lock (Requested)
                    Requested.Add(url);

                byte[] body;
                string head;
                if (Files.TryGetValue(url, out var file))
                {
                    body = file;
                    head = "HTTP/1.1 200 OK\r\nETag: \"loopback\"\r\n";
                }
                else
                {
                    body = [];
                    head = "HTTP/1.1 404 Not Found\r\n";
                }

                var header = Encoding.ASCII.GetBytes(head + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            _stop.Dispose();
        }
    }

    /// <summary>Test-only: sends https://github.com/... and https://api.github.com/... to the loopback server.</summary>
    private sealed class ToLoopback(int port) : DelegatingHandler(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
    })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.RequestUri!;
            Assert.Equal("https", original.Scheme);
            Assert.True(original.Host is "github.com" or "api.github.com", original.Host);
            request.RequestUri = new Uri($"http://127.0.0.1:{port}/{original.Host}{original.PathAndQuery}");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed record Release(string Asset, byte[] Payload, byte[] Sums, byte[]? Signature);

    private static Release Signed(TestKey key, InstallKind kind, byte[]? payload = null, byte[]? sumsPayload = null)
    {
        var asset = ReleaseDocumentParser.AssetNameFor(kind, new ReleaseVersion(1, 2, 1));
        payload ??= kind == InstallKind.AppImage ? [.. Elf, .. "new AppImage"u8] : Encoding.UTF8.GetBytes("package " + kind);
        var digest = Convert.ToHexString(SHA256.HashData(sumsPayload ?? payload)).ToLowerInvariant();
        var sums = Encoding.UTF8.GetBytes($"{digest}  {asset}\n");
        return new Release(asset, payload, sums, key.Sign(sums, "realm-portal " + Tag));
    }

    private static void Publish(LoopbackFeed feed, Release release, bool listSignature = true)
    {
        var assets = new List<object>
        {
            new { name = release.Asset, browser_download_url = Download(release.Asset), size = release.Payload.Length },
            new { name = "SHA256SUMS", browser_download_url = Download("SHA256SUMS"), size = release.Sums.Length },
        };
        if (listSignature)
            assets.Add(new { name = ReleaseSignature.AssetName, browser_download_url = Download(ReleaseSignature.AssetName), size = 300 });

        feed.Files[GitHubReleaseFeed.LatestReleaseUri.ToString()] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = Tag,
            draft = false,
            prerelease = false,
            body = "notes",
            html_url = "https://github.com/KarloZ7715/realm-portal/releases/tag/" + Tag,
            assets,
        });
        feed.Files[Download("SHA256SUMS")] = release.Sums;
        feed.Files[Download(release.Asset)] = release.Payload;
        if (release.Signature is not null)
            feed.Files[Download(ReleaseSignature.AssetName)] = release.Signature;
    }

    private sealed record Outcome(
        UpdateInstallResult? Result, UpdateException? Error, string Downloads, string? AppImage, string CacheHome);

    private const UnixFileMode Mode0755 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static UnixFileMode Octal(string mode) => (UnixFileMode)Convert.ToInt32(mode, 8);

    private static async Task<Outcome> Install(
        LoopbackFeed feed, TempDirectory dir, TestKey trusted, InstallKind kind, bool selfReplace = false,
        UnixFileMode appsMode = Mode0755, UnixFileMode appImageMode = Mode0755,
        UnixFileMode? downloadsMode = null, UnixFileMode? cacheHomeMode = null,
        string? appsGroup = null, string? appImageGroup = null, Action<string, string>? configureApps = null)
    {
        var config = dir.Combine("config");
        var downloads = dir.Combine("downloads");
        var cacheHome = dir.Combine("cache-home");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(cacheHome);
        if (downloadsMode is { } dm && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(downloads, dm);
        if (cacheHomeMode is { } cm && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(cacheHome, cm);

        string? appImage = null;
        if (kind == InstallKind.AppImage && selfReplace)
        {
            var apps = dir.Combine("apps");
            Directory.CreateDirectory(apps);
            appImage = Path.Combine(apps, "RealmPortal.AppImage");
            File.WriteAllBytes(appImage, [.. Elf, .. "old AppImage"u8]);
            // Explicit modes: the result must not depend on the umask of the machine running the tests.
            File.SetUnixFileMode(appImage, appImageMode);
            File.SetUnixFileMode(apps, appsMode);
            if (appsGroup is not null)
                Run("sudo", "-n", "chgrp", appsGroup, apps);
            if (appImageGroup is not null)
                Run("sudo", "-n", "chgrp", appImageGroup, appImage);
            configureApps?.Invoke(apps, appImage);
        }

        var configLock = new ConfigDirectoryLock(config);
        var writer = new AtomicFileWriter();
        using var http = new HttpClient(new ToLoopback(feed.Port));
        using var releases = new GitHubReleaseFeed(http);
        using var downloader = new UpdateDownloader(
            http,
            getVariable: name => name switch
            {
                "APPIMAGE" => appImage,
                "XDG_CACHE_HOME" => cacheHome,
                _ => null,
            },
            installKind: () => kind,
            appImageSelfReplace: () => selfReplace,
            downloadsDirectory: () => downloads,
            signatureVerifier: new MinisignVerifier([trusted.PublicKey]));
        var checker = new UpdateChecker(
            new NetworkSettingsStore(config, writer, configLock),
            new UpdateCacheStore(config, writer, configLock),
            releases,
            downloader,
            runningVersion: () => Running);

        var check = await checker.CheckAsync(UpdateCheckMode.Manual);
        Assert.Equal(UpdateCheckOutcome.Offer, check.Outcome);

        try
        {
            return new Outcome(await checker.InstallUpdateAsync(check.Offer!), null, downloads, appImage, cacheHome);
        }
        catch (UpdateException ex)
        {
            return new Outcome(null, ex, downloads, appImage, cacheHome);
        }
    }

    private static void AssertNothingPlaced(Outcome outcome)
    {
        Assert.Null(outcome.Result);
        Assert.Empty(Directory.GetFiles(outcome.Downloads, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(InstallKind.Deb)]
    [InlineData(InstallKind.WindowsZip)]
    [InlineData(InstallKind.AppImage)]
    public async Task ValidSignature_PlacesTheVerifiedPackageInDownloads(InstallKind kind)
    {
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, kind);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, kind);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Result);
        Assert.Equal(kind, outcome.Result.Kind);
        Assert.False(outcome.Result.ReplacedAppImage);
        Assert.Equal(Path.Combine(outcome.Downloads, release.Asset), outcome.Result.SavedPath);
        Assert.Equal(release.Payload, File.ReadAllBytes(outcome.Result.SavedPath));
        Assert.Empty(Directory.GetFiles(outcome.Downloads, "*.part"));
        Assert.Contains(Download(ReleaseSignature.AssetName), feed.Requested);
        if (kind == InstallKind.Deb)
            Assert.Contains("apt install", outcome.Result.CopyableCommand);
    }

    [Fact]
    public async Task ValidSignature_AppImageSelfReplace_ReplacesTheRunningAppImage()
    {
        if (OperatingSystem.IsWindows())
            return; // AppImages are Linux only; the replace path uses link(2) and Unix modes.

        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Result);
        Assert.True(outcome.Result.ReplacedAppImage);
        Assert.Equal(outcome.AppImage, outcome.Result.SavedPath);
        Assert.Equal(release.Payload, File.ReadAllBytes(outcome.AppImage!));
        Assert.Equal([.. Elf, .. "old AppImage"u8], File.ReadAllBytes(outcome.Result.BackupPath!));
        AssertNothingPlaced(outcome with { Result = null });
    }

    [Fact]
    public async Task MissingSignature_IsRefusedBeforeThePackageIsFetched()
    {
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.Deb) with { Signature = null };
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.Deb);

        Assert.Equal(UpdateErrorCode.SignatureMissing, outcome.Error?.Code);
        Assert.True(UpdateInstallPresentation.IsSignatureFailure(outcome.Error));
        AssertNothingPlaced(outcome);
        Assert.DoesNotContain(Download(release.Asset), feed.Requested);
    }

    [Fact]
    public async Task InvalidSignature_IsRefusedBeforeThePackageIsFetched()
    {
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var trusted = new TestKey();
        var release = Signed(new TestKey(), InstallKind.Deb);
        Publish(feed, release);

        var outcome = await Install(feed, dir, trusted, InstallKind.Deb);

        Assert.Equal(UpdateErrorCode.SignatureInvalid, outcome.Error?.Code);
        Assert.Equal("update.install.badsignature.body", UpdateInstallPresentation.FailureBodyKey(outcome.Error));
        AssertNothingPlaced(outcome);
        Assert.DoesNotContain(Download(release.Asset), feed.Requested);
    }

    [Fact]
    public async Task SignedSumsButDifferentPackage_IsAChecksumMismatch()
    {
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.Deb, sumsPayload: "the package that was signed"u8.ToArray());
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.Deb);

        Assert.Equal(UpdateErrorCode.ChecksumMismatch, outcome.Error?.Code);
        Assert.False(UpdateInstallPresentation.IsSignatureFailure(outcome.Error));
        AssertNothingPlaced(outcome);
        Assert.Contains(Download(release.Asset), feed.Requested);
    }

    private static readonly byte[] OldAppImage = [.. Elf, .. "old AppImage"u8];
    private static readonly byte[] Attacker = "UNSIGNED LOCAL PAYLOAD"u8.ToArray();


    private static void Run(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(args[0]) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args.Skip(1))
            psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static bool TrySetAccessAcl(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("setfacl")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "-m", "u:nobody:rwx", path })
                psi.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static IDisposable UsePrivateGroupLookup()
    {
        var name = Environment.UserName;
        var uid = UnixPathSafety.EffectiveUserId;
        UnixPathSafety.LookupPasswdForTests = id => id == uid ? new UnixPasswd(name, uid, uid) : null;
        UnixPathSafety.LookupGroupForTests = id => id == uid ? new UnixGroup(name, uid, Array.Empty<string>()) : null;
        UnixPathSafety.EnumeratePasswdForTests = () => [new UnixPasswd(name, uid, uid)];
        return new ClearAccountLookup();
    }

    private sealed class ClearAccountLookup : IDisposable
    {
        public void Dispose()
        {
            UnixPathSafety.LookupPasswdForTests = null;
            UnixPathSafety.LookupGroupForTests = null;
            UnixPathSafety.EnumeratePasswdForTests = null;
        }
    }

    private static bool CanRunAsNobody()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sudo") { UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in new[] { "-n", "-u", "nobody", "true" })
                psi.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>As another uid (nobody): unlink <paramref name="path"/> and put unsigned bytes in its place. Returns the exit code.</summary>
    private static int SwapAsNobody(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("sudo") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in new[] { "-n", "-u", "nobody", "python3", "-c",
                     "import os,sys; p=sys.argv[1]; os.unlink(p); open(p,'wb').write(b'UNSIGNED LOCAL PAYLOAD'); os.chmod(p,0o755)", path })
            psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void AssertReleasePageFallback(Outcome outcome, string appImage)
    {
        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Result);
        Assert.Equal(InstallKind.Unknown, outcome.Result.Kind);
        Assert.False(outcome.Result.ReplacedAppImage);
        Assert.True(UpdateInstallOutcome.FromCore(outcome.Result).IsReleasePageFallback);
        Assert.Equal(OldAppImage, File.ReadAllBytes(appImage));
        var folder = Path.GetDirectoryName(appImage)!;
        Assert.Empty(Directory.GetDirectories(folder, ".RealmPortal-staging-*"));
        Assert.Empty(Directory.GetFiles(folder, ".RealmPortal-*.part", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Security review regression (#206): the AppImage folder becomes world-writable after the target was resolved, and
    /// another uid tries to swap the verified file before the replace. The update must fail closed: the AppImage keeps
    /// its old bytes and the app falls back to the release page. (Before the fix the unsigned bytes were installed.)
    /// </summary>
    [Fact]
    public async Task Review206_OtherUidCannotSwapVerifiedPartInSharedDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        Publish(feed, Signed(key, InstallKind.AppImage));
        Directory.CreateDirectory(dir.Combine("apps"));
        File.WriteAllText(dir.Combine("apps", $".RealmPortal-{RealmPortal.Core.AppInfo.Version}.AppImage.bak"), "old backup");
        var canSwap = CanRunAsNobody();
        var probes = 0;
        AppImageTarget.IdentityProbeForTests = (string path, out ulong device, out ulong inode, out int mode, out bool owned) =>
        {
            if (++probes == 1)
                File.SetUnixFileMode(Path.GetDirectoryName(path)!, Octal("777"));
            if (probes == 3 && canSwap)
            {
                // Wherever the downloaded file sits under the AppImage folder, the other uid tries to swap it.
                foreach (var part in Directory.GetFiles(Path.GetDirectoryName(path)!, ".RealmPortal-*.part", SearchOption.AllDirectories))
                    _ = SwapAsNobody(part);
            }
            var probe = AppImageTarget.IdentityProbeForTests;
            AppImageTarget.IdentityProbeForTests = null;
            try { return AppImageTarget.TryGetIdentity(path, out device, out inode, out mode, out owned); }
            finally { AppImageTarget.IdentityProbeForTests = probe; }
        };
        try
        {
            var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true);
            Assert.NotEqual(Attacker, File.ReadAllBytes(outcome.AppImage!));
            AssertReleasePageFallback(outcome, outcome.AppImage!);
        }
        finally { AppImageTarget.IdentityProbeForTests = null; }
    }

    /// <summary>
    /// In a sticky world-writable (1777) folder the verified file is still staged in a private 0700 directory:
    /// another uid cannot swap it, and the signed bytes are what get installed. This whole test returns when
    /// <c>sudo -n -u nobody</c> is not available (it does not skip only the swap and keep going).
    /// On the pre-fix code the <c>.part</c> sat in the 1777 folder and the swap succeeded.
    /// </summary>
    [Fact]
    public async Task VerifiedFile_IsStagedWhereAnotherUidCannotSwapIt()
    {
        if (OperatingSystem.IsWindows() || !CanRunAsNobody()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);
        var attempts = new List<int>();
        var partParents = new List<(UnixFileMode Mode, string Name)>();
        AppImageTarget.IdentityProbeForTests = (string path, out ulong device, out ulong inode, out int mode, out bool owned) =>
        {
            foreach (var part in Directory.GetFiles(Path.GetDirectoryName(path)!, ".RealmPortal-*.part", SearchOption.AllDirectories))
            {
                attempts.Add(SwapAsNobody(part));
                var parent = Path.GetDirectoryName(part)!;
                partParents.Add((File.GetUnixFileMode(parent), Path.GetFileName(parent)));
            }
            var probe = AppImageTarget.IdentityProbeForTests;
            AppImageTarget.IdentityProbeForTests = null;
            try { return AppImageTarget.TryGetIdentity(path, out device, out inode, out mode, out owned); }
            finally { AppImageTarget.IdentityProbeForTests = probe; }
        };
        try
        {
            var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true, appsMode: Octal("1777"));
            Assert.Null(outcome.Error);
            Assert.True(outcome.Result!.ReplacedAppImage);
            Assert.NotEmpty(attempts);
            Assert.All(attempts, code => Assert.NotEqual(0, code));
            // Sticky 1777 already blocks unlink of another user's file, so a swap-only check still
            // passes on the old code. The .part must live in a 0700 staging directory (it sat in
            // the 1777 folder before this fix).
            Assert.NotEmpty(partParents);
            const UnixFileMode mode0700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.All(partParents, parent =>
            {
                Assert.Equal(mode0700, parent.Mode & (UnixFileMode)0x1FF);
                Assert.StartsWith(".RealmPortal-staging-", parent.Name, StringComparison.Ordinal);
            });
            Assert.Equal(release.Payload, File.ReadAllBytes(outcome.AppImage!));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(outcome.AppImage!)!, ".RealmPortal-staging-*"));
        }
        finally { AppImageTarget.IdentityProbeForTests = null; }
    }

    [Theory]
    [InlineData("775")] // group-writable
    [InlineData("757")] // other-writable
    [InlineData("777")]
    public async Task FolderWritableByOthers_FailsClosedToTheReleasePage(string appsMode)
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);

        var outcome = await Install(
            feed, dir, key, InstallKind.AppImage, selfReplace: true, appsMode: Octal(appsMode),
            appsGroup: appsMode == "775" ? "nogroup" : null);

        AssertReleasePageFallback(outcome, outcome.AppImage!);
        Assert.DoesNotContain(Download(release.Asset), feed.Requested);
        Assert.Empty(Directory.GetFiles(outcome.Downloads, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("775")]
    [InlineData("757")]
    public async Task AppImageWritableByOthers_FailsClosedToTheReleasePage(string appImageMode)
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        Publish(feed, Signed(key, InstallKind.AppImage));

        var outcome = await Install(
            feed, dir, key, InstallKind.AppImage, selfReplace: true, appImageMode: Octal(appImageMode),
            appImageGroup: appImageMode == "775" ? "nogroup" : null);

        AssertReleasePageFallback(outcome, outcome.AppImage!);
    }

    [Fact]
    public async Task AppImage775_WithPrivateGroup_Replaces()
    {
        if (OperatingSystem.IsWindows()) return;
        using var hook = UsePrivateGroupLookup();
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true, appImageMode: Octal("775"));

        Assert.Null(outcome.Error);
        Assert.True(outcome.Result!.ReplacedAppImage);
        Assert.Equal(release.Payload, File.ReadAllBytes(outcome.AppImage!));
    }

    [Fact]
    public async Task StickyWorldWritableFolder_StillReplaces()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true, appsMode: Octal("1777"));

        Assert.Null(outcome.Error);
        Assert.True(outcome.Result!.ReplacedAppImage);
        Assert.Equal(release.Payload, File.ReadAllBytes(outcome.AppImage!));
    }

    [Fact]
    public async Task DownloadsPlacement_LeavesNoStagingDirectory()
    {
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.Deb);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.Deb);

        Assert.Null(outcome.Error);
        Assert.Equal([Path.Combine(outcome.Downloads, release.Asset)], Directory.GetFileSystemEntries(outcome.Downloads));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(outcome.Result!.SavedPath));
    }

    /// <summary>
    /// Changing the folder to world-writable after the download must still fail closed. Removing the
    /// "right before replace" safety check would let this install succeed.
    /// </summary>
    [Fact]
    public async Task FolderBecomesUnsafeAfterDownload_FailsClosedToTheReleasePage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        Publish(feed, Signed(key, InstallKind.AppImage));
        AppImageTarget.IdentityProbeForTests = (string path, out ulong device, out ulong inode, out int mode, out bool owned) =>
        {
            var folder = Path.GetDirectoryName(path)!;
            if (Directory.GetFiles(folder, ".RealmPortal-*.part", SearchOption.AllDirectories).Length > 0)
                File.SetUnixFileMode(folder, Octal("777"));
            var probe = AppImageTarget.IdentityProbeForTests;
            AppImageTarget.IdentityProbeForTests = null;
            try { return AppImageTarget.TryGetIdentity(path, out device, out inode, out mode, out owned); }
            finally { AppImageTarget.IdentityProbeForTests = probe; }
        };
        try
        {
            var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true);
            AssertReleasePageFallback(outcome, outcome.AppImage!);
        }
        finally { AppImageTarget.IdentityProbeForTests = null; }
    }

    [Fact]
    public async Task Folder775_WithPrivateGroup_Replaces()
    {
        if (OperatingSystem.IsWindows()) return;
        using var hook = UsePrivateGroupLookup();
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.AppImage, selfReplace: true, appsMode: Octal("775"));

        Assert.Null(outcome.Error);
        Assert.True(outcome.Result!.ReplacedAppImage);
        Assert.Equal(release.Payload, File.ReadAllBytes(outcome.AppImage!));
    }

    [Fact]
    public async Task Folder775_WithAcl_FailsClosedToTheReleasePage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var hook = UsePrivateGroupLookup();
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.AppImage);
        Publish(feed, release);
        var aclApplied = false;

        var outcome = await Install(
            feed, dir, key, InstallKind.AppImage, selfReplace: true, appsMode: Octal("775"),
            configureApps: (apps, _) => aclApplied = TrySetAccessAcl(apps));

        if (!aclApplied)
        {
            // setfacl is not available or failed; the ACL fail-closed case is not exercised on this host.
            return;
        }

        AssertReleasePageFallback(outcome, outcome.AppImage!);
        Assert.DoesNotContain(Download(release.Asset), feed.Requested);
    }

    [Fact]
    public async Task DownloadsWritableByOthers_UsesPrivateCacheDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.Deb);
        Publish(feed, release);

        var outcome = await Install(feed, dir, key, InstallKind.Deb, downloadsMode: Octal("777"));

        Assert.Null(outcome.Error);
        Assert.Equal(InstallKind.Deb, outcome.Result!.Kind);
        var expected = Path.Combine(outcome.CacheHome, "RealmPortal", "updates", release.Asset);
        Assert.Equal(expected, outcome.Result.SavedPath);
        Assert.Equal(release.Payload, File.ReadAllBytes(expected));
        Assert.Empty(Directory.GetFiles(outcome.Downloads, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadsAndCacheUnsafe_FallsBackToTheReleasePage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDirectory();
        using var feed = new LoopbackFeed();
        var key = new TestKey();
        var release = Signed(key, InstallKind.Deb);
        Publish(feed, release);

        var outcome = await Install(
            feed, dir, key, InstallKind.Deb, downloadsMode: Octal("777"), cacheHomeMode: Octal("777"));

        Assert.Null(outcome.Error);
        Assert.Equal(InstallKind.Unknown, outcome.Result!.Kind);
        Assert.True(UpdateInstallOutcome.FromCore(outcome.Result).IsReleasePageFallback);
        Assert.Empty(Directory.GetFiles(outcome.Downloads, "*", SearchOption.AllDirectories));
        Assert.DoesNotContain(Download(release.Asset), feed.Requested);
    }

}
