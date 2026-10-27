using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Updates;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class UpdateNotice105Tests
{
    private static string ReleaseJson(
        string tag,
        bool draft = false,
        bool prerelease = false,
        string? body = "notes",
        string? htmlUrl = null,
        IEnumerable<(string name, string url, long size)>? assets = null)
    {
        htmlUrl ??= $"https://github.com/KarloZ7715/realm-portal/releases/tag/{tag}";
        assets ??= [];
        var assetArr = assets.Select(a => new
        {
            name = a.name,
            browser_download_url = a.url,
            size = a.size,
        }).ToArray();
        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            body,
            html_url = htmlUrl,
            assets = assetArr,
        });
    }

    private sealed class FakeFeed : IReleaseFeed
    {
        public Func<string?, CancellationToken, Task<ReleaseFeedResponse>>? Handler { get; set; }
        public int Calls { get; private set; }
        public string? LastIfNoneMatch { get; private set; }
        public List<HttpRequestMessage> Captured { get; } = [];

        public Task<ReleaseFeedResponse> GetLatestAsync(string? ifNoneMatch, CancellationToken cancellationToken)
        {
            Calls++;
            LastIfNoneMatch = ifNoneMatch;
            return Handler!(ifNoneMatch, cancellationToken);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Copy headers of interest before the request is disposed upstream.
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var h in request.Headers)
                copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
            Requests.Add(copy);
            return Task.FromResult(Responder!(request));
        }
    }

    private static (NetworkSettingsStore settings, UpdateCacheStore cache, TempDirectory dir) Stores()
    {
        var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var lock_ = new ConfigDirectoryLock(config);
        var writer = new AtomicFileWriter();
        return (new NetworkSettingsStore(config, writer, lock_), new UpdateCacheStore(config, writer, lock_), dir);
    }

    [Fact]
    public async Task Automatic_SkipsWhenAutoCheckOff()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            settings.Save(new NetworkSettings(AutoCheckUpdates: false));
            var feed = new FakeFeed
            {
                Handler = (_, _) => throw new InvalidOperationException("should not be called"),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1");
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.SkippedByPolicy, result.Outcome);
            Assert.Equal(0, feed.Calls);
        }
    }

    [Fact]
    public async Task Automatic_SkipsWithin24HoursIncludingFailedAttempt()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
            settings.Save(new NetworkSettings(AutoCheckUpdates: true, LastAttemptUtc: now.AddHours(-1)));
            var feed = new FakeFeed { Handler = (_, _) => throw new InvalidOperationException("no") };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1", clock: () => now);
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.SkippedByPolicy, result.Outcome);
            Assert.Equal(0, feed.Calls);
        }
    }

    [Fact]
    public async Task Manual_WorksWhenAutoCheckOff_AndDebounces()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            settings.Save(new NetworkSettings(AutoCheckUpdates: false));
            var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
            var clock = now;
            var json = ReleaseJson("v1.1.0");
            var feed = new FakeFeed
            {
                Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, "\"etag1\"", json, false)),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1", clock: () => clock);
            var first = await checker.CheckAsync(UpdateCheckMode.Manual);
            Assert.Equal(UpdateCheckOutcome.Offer, first.Outcome);
            Assert.Equal("1.1.0", first.Offer!.Version);

            clock = now.AddSeconds(30);
            var second = await checker.CheckAsync(UpdateCheckMode.Manual);
            Assert.Equal(UpdateCheckOutcome.SkippedByPolicy, second.Outcome);
            Assert.Equal(1, feed.Calls);

            clock = now.AddMinutes(1);
            var third = await checker.CheckAsync(UpdateCheckMode.Manual);
            Assert.Equal(2, feed.Calls);
            Assert.Equal(UpdateCheckOutcome.Offer, third.Outcome);
        }
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.1", UpdateCheckOutcome.Offer)]
    [InlineData("v1.0.1", "1.0.1", UpdateCheckOutcome.UpToDate)]
    [InlineData("v1.0.0", "1.0.1", UpdateCheckOutcome.UpToDate)]
    public async Task VersionComparison(string tag, string running, UpdateCheckOutcome expected)
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var feed = new FakeFeed
            {
                Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, null, ReleaseJson(tag), false)),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => running,
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            settings.Save(new NetworkSettings(AutoCheckUpdates: true, LastAttemptUtc: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(expected, result.Outcome);
        }
    }

    [Fact]
    public async Task SkippedVersion_NoNotice()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            settings.Save(new NetworkSettings(AutoCheckUpdates: true, SkippedVersion: "1.1.0",
                LastAttemptUtc: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
            var feed = new FakeFeed
            {
                Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, null, ReleaseJson("v1.1.0"), false)),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1",
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.NoNotice, result.Outcome);
        }
    }

    [Fact]
    public async Task Later_PostponesSameVersion_ButNewerStillOffered()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
            var feed = new FakeFeed
            {
                Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, null, ReleaseJson("v1.1.0"), false)),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1", clock: () => now);
            checker.RemindLater("1.1.0");
            settings.Save(settings.Load() with { LastAttemptUtc = now.AddDays(-2), AutoCheckUpdates = true });
            var postponed = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.NoNotice, postponed.Outcome);

            feed.Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, null, ReleaseJson("v1.2.0"), false));
            settings.Update(s => s with { LastAttemptUtc = now.AddDays(-2) });
            var newer = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.Offer, newer.Outcome);
            Assert.Equal("1.2.0", newer.Offer!.Version);
        }
    }

    [Theory]
    [InlineData(true, false, "v1.1.0")] // draft
    [InlineData(false, true, "v1.1.0")] // prerelease
    [InlineData(false, false, "v1.1.0-rc.1")] // bad tag
    [InlineData(false, false, "v01.1.0")] // leading zero
    public async Task IneligibleReleases_NoNotice(bool draft, bool pre, string tag)
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            settings.Save(new NetworkSettings(LastAttemptUtc: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
            var feed = new FakeFeed
            {
                Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(200, null, ReleaseJson(tag, draft, pre), false)),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.0",
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.NoNotice, result.Outcome);
        }
    }

    [Fact]
    public async Task NotModified_ReusesCache_WithoutCacheDropsEtag()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var json = ReleaseJson("v1.1.0");
            cache.Save("\"abc\"", json);
            settings.Save(new NetworkSettings(ETag: "\"abc\"", LastAttemptUtc: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
            var feed = new FakeFeed
            {
                Handler = (etag, _) =>
                {
                    Assert.Equal("\"abc\"", etag);
                    return Task.FromResult(new ReleaseFeedResponse(304, "\"abc\"", null, true));
                },
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1",
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.Offer, result.Outcome);

            // Without cache → 304 fails
            cache.Clear();
            settings.Save(settings.Load() with { LastAttemptUtc = DateTimeOffset.Parse("2026-10-20T12:00:00Z"), ETag = "\"abc\"" });
            feed.Handler = (_, _) => Task.FromResult(new ReleaseFeedResponse(304, "\"abc\"", null, true));
            var fail = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.Failed, fail.Outcome);
            Assert.Null(settings.Load().ETag);
        }
    }

    [Fact]
    public async Task RateLimited_SurfacesAndHonoursRetry()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
            var clock = now;
            settings.Save(new NetworkSettings(LastAttemptUtc: now.AddDays(-2)));
            var feed = new FakeFeed
            {
                Handler = (_, _) => throw new UpdateException(UpdateErrorCode.RateLimited, "limited")
                {
                    HttpStatus = 429,
                    RetryAfter = TimeSpan.FromMinutes(5),
                },
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1", clock: () => clock);
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.RateLimited, result.Outcome);

            clock = now.AddMinutes(1);
            settings.Update(s => s with { LastAttemptUtc = now.AddDays(-2) });
            var again = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.RateLimited, again.Outcome);
            Assert.Equal(1, feed.Calls); // second call blocked by local gate
        }
    }

    [Fact]
    public async Task OversizedBody_Fails()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            settings.Save(new NetworkSettings(LastAttemptUtc: DateTimeOffset.Parse("2026-10-20T12:00:00Z")));
            var feed = new FakeFeed
            {
                Handler = (_, _) => throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "too big"),
            };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1",
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            var result = await checker.CheckAsync(UpdateCheckMode.Automatic);
            Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
            Assert.Equal(UpdateErrorCode.DownloadTooLarge, result.ErrorCode);
        }
    }

    [Theory]
    [InlineData("http://api.github.com/repos/x/y")]
    [InlineData("https://evilgithubusercontent.com/x")]
    [InlineData("https://github.com.evil.example/x")]
    [InlineData("https://user:pass@api.github.com/x")]
    [InlineData("https://api.github.com:8443/x")]
    public void TrustedHttpsUrl_RejectsLookalikes(string url)
    {
        Assert.False(TrustedHttpsUrl.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/KarloZ7715/realm-portal/releases/latest")]
    [InlineData("https://github.com/KarloZ7715/realm-portal/releases/download/v1.1.0/x")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/x")]
    public void TrustedHttpsUrl_AllowsAllowlist(string url)
    {
        Assert.True(TrustedHttpsUrl.IsAllowed(new Uri(url)));
    }

    [Fact]
    public async Task GitHubReleaseFeed_NeverSendsAuthorization_EvenWithEnvTokens()
    {
        var handler = new RecordingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ReleaseJson("v1.1.0"), Encoding.UTF8, "application/json"),
            },
        };
        var priorGh = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var priorTok = Environment.GetEnvironmentVariable("GH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-should-not-be-sent");
            Environment.SetEnvironmentVariable("GH_TOKEN", "also-secret");
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var feed = new GitHubReleaseFeed(client, () => "1.0.1");
            await feed.GetLatestAsync(null, CancellationToken.None);
            Assert.Single(handler.Requests);
            var req = handler.Requests[0];
            Assert.Null(req.Headers.Authorization);
            Assert.False(req.Headers.Contains("Authorization"));
            Assert.Contains(req.Headers, h => h.Key == "User-Agent" && h.Value.Single().StartsWith("RealmPortal/"));
            Assert.Contains(req.Headers, h => h.Key == "Accept");
            Assert.Contains(req.Headers, h => h.Key == "X-GitHub-Api-Version");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", priorGh);
            Environment.SetEnvironmentVariable("GH_TOKEN", priorTok);
        }
    }

    [Fact]
    public void Sha256Sums_RequiresExactlyOneMatchingLine()
    {
        var name = "RealmPortal-1.1.0-x86_64.AppImage";
        var digest = Convert.ToHexString(SHA256.HashData("hello"u8.ToArray())).ToLowerInvariant();
        var text = $"{digest}  {name}\n";
        var found = Sha256Sums.FindDigest(text, name);
        Assert.Equal(digest, Sha256Sums.FormatHex(found));

        Assert.Throws<UpdateException>(() => Sha256Sums.FindDigest($"{digest}  {name}\n{digest}  {name}\n", name));
        Assert.Throws<UpdateException>(() => Sha256Sums.FindDigest($"{digest}  other\n", name));
    }

    [Fact]
    public void ShellCommandQuote_HandlesSpacesQuotesAndDollar_RejectsControls()
    {
        var q = ShellCommandQuote.QuoteAbsolutePath("/tmp/my file's $dir/x.deb");
        Assert.Equal("'/tmp/my file'\\''s $dir/x.deb'", q);
        Assert.Null(ShellCommandQuote.QuoteAbsolutePath("/tmp/bad\nname.deb"));
        Assert.Equal("sudo apt install '/tmp/a.deb'", ShellCommandQuote.AptInstallCommand("/tmp/a.deb"));
        Assert.Equal("chmod u+x '/tmp/a.AppImage'", ShellCommandQuote.ChmodUxCommand("/tmp/a.AppImage"));
    }

    [Fact]
    public void NetworkSettingsStore_DefaultsAndRoundTrip()
    {
        var (settings, _, dir) = Stores();
        using (dir)
        {
            var d = settings.Load();
            Assert.True(d.AutoCheckUpdates);
            Assert.True(d.ProbeEnabled);
            settings.Save(d with { AutoCheckUpdates = false, ProbeEnabled = false, SkippedVersion = "1.2.0" });
            var loaded = settings.Load();
            Assert.False(loaded.AutoCheckUpdates);
            Assert.False(loaded.ProbeEnabled);
            Assert.Equal("1.2.0", loaded.SkippedVersion);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(settings.FilePath);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            }
        }
    }

    [Fact]
    public void XdgDownloads_NeverOverwrites()
    {
        using var dir = new TempDirectory();
        var folder = dir.Combine("dl");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.deb"), "1");
        var path = XdgDownloads.AllocateNoncollidingPath(folder, "a.deb");
        Assert.Equal(Path.Combine(folder, "a (1).deb"), path);
    }

    [Fact]
    public void ReleaseVersion_ParsesInformationalWithoutMetadata()
    {
        Assert.True(ReleaseVersion.TryParseInformational("1.0.1+git", out var v));
        Assert.Equal("1.0.1", v.WithoutPrefix);
        Assert.False(ReleaseVersion.TryParseInformational("1.0.1-rc.1", out _));
        Assert.False(ReleaseVersion.TryParseTag("v1.0.1-beta", out _));
    }

    [Fact]
    public void AppImageMode_MasksTo0755()
    {
        // setuid | group write | other write should be dropped
        var original = 0b100_000_000_111_111_111; // include high bits then mask
        var masked = AppImageTarget.ModeMasked0755(Convert.ToInt32("4777", 8));
        Assert.Equal(Convert.ToInt32("755", 8), masked);
    }

    [Fact]
    public async Task Download_RedirectUntrustedHost_Fails()
    {
        var handler = new RecordingHandler
        {
            Responder = req =>
            {
                if (req.RequestUri!.Host == "github.com")
                {
                    return new HttpResponseMessage(HttpStatusCode.Found)
                    {
                        Headers = { Location = new Uri("https://evil.example/asset") },
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        using var client = new HttpClient(handler);
        using var dir = new TempDirectory();
        var downloads = dir.Combine("dl");
        Directory.CreateDirectory(downloads);
        var dl = new UpdateDownloader(
            client,
            installKind: () => InstallKind.Deb,
            appImageSelfReplace: () => false,
            downloadsDirectory: () => downloads);

        var version = new ReleaseVersion(1, 1, 0);
        var asset = "realm-portal_1.1.0_amd64.deb";
        var url = ReleaseDocumentParser.ExpectedDownloadUrl(version, asset);
        var release = new ReleaseDocument(version, "v1.1.0", "", null,
            [new ReleaseAsset(asset, url, 10)]);

        // Need SHA256SUMS to succeed first — make handler serve sums then redirect asset
        handler.Responder = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("SHA256SUMS", StringComparison.Ordinal))
            {
                var digest = Convert.ToHexString(SHA256.HashData("payload"u8.ToArray())).ToLowerInvariant();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{digest}  {asset}\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://evil.example/asset") },
            };
        };

        var ex = await Assert.ThrowsAsync<UpdateException>(() => dl.DownloadAndPlaceAsync(release));
        Assert.Equal(UpdateErrorCode.UntrustedRedirect, ex.Code);
    }

    [Fact]
    public async Task Download_Deb_VerifiesAndPlacesWithoutExecuting()
    {
        var payload = "deb-payload-bytes"u8.ToArray();
        var digest = SHA256.HashData(payload);
        var hex = Convert.ToHexString(digest).ToLowerInvariant();
        var version = new ReleaseVersion(1, 1, 0);
        var asset = "realm-portal_1.1.0_amd64.deb";
        var url = ReleaseDocumentParser.ExpectedDownloadUrl(version, asset);
        var sumsUrl = ReleaseDocumentParser.ExpectedDownloadUrl(version, "SHA256SUMS");

        var handler = new RecordingHandler
        {
            Responder = req =>
            {
                var u = req.RequestUri!.ToString();
                if (u == sumsUrl)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Encoding.UTF8.GetBytes($"{hex}  {asset}\n")),
                    };
                }

                if (u == url)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(payload)
                        {
                            Headers = { ContentLength = payload.Length },
                        },
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            },
        };

        using var client = new HttpClient(handler);
        using var dir = new TempDirectory();
        var downloads = dir.Combine("dl");
        Directory.CreateDirectory(downloads);
        using var dl = new UpdateDownloader(
            client,
            installKind: () => InstallKind.Deb,
            appImageSelfReplace: () => false,
            downloadsDirectory: () => downloads);

        var release = new ReleaseDocument(version, "v1.1.0", "notes", null,
            [new ReleaseAsset(asset, url, payload.Length)]);
        var result = await dl.DownloadAndPlaceAsync(release);
        Assert.Equal(InstallKind.Deb, result.Kind);
        Assert.True(File.Exists(result.SavedPath));
        Assert.Equal(payload, File.ReadAllBytes(result.SavedPath));
        Assert.StartsWith("sudo apt install '", result.CopyableCommand);
        Assert.Contains(Path.GetFullPath(result.SavedPath), result.CopyableCommand);
        Assert.DoesNotContain("pkexec", result.CopyableCommand);
        Assert.False(result.ReplacedAppImage);
    }

    [Fact]
    public void SkipAndRemindLater_Persist()
    {
        var (settings, cache, dir) = Stores();
        using (dir)
        {
            var feed = new FakeFeed { Handler = (_, _) => throw new InvalidOperationException() };
            var checker = new UpdateChecker(settings, cache, feed, runningVersion: () => "1.0.1",
                clock: () => DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
            checker.SkipThisVersion("1.1.0");
            Assert.Equal("1.1.0", settings.Load().SkippedVersion);
            checker.RemindLater("v1.2.0");
            var s = settings.Load();
            Assert.Equal("1.2.0", s.LaterVersion);
            Assert.NotNull(s.LaterUntilUtc);
            Assert.True(s.LaterUntilUtc > DateTimeOffset.Parse("2026-10-29T12:00:00Z"));
        }
    }
}
