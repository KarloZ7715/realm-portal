using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using RealmPortal.App.Network;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;
using RealmPortal.Core.Updates;
using RealmPortal.Core.Updates.Signing;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>
/// End to end for #116: the real GitHubReleaseFeed, UpdateChecker and UpdateDownloader run against a simulated GitHub
/// (an in-process HttpMessageHandler, because the updater only talks to the github.com HTTPS allowlist, so a loopback
/// server would be refused). The release is signed in the test with a throwaway key in minisign's ED format.
/// Only the existing constructor parameters are used to swap the HTTP client and the trusted key.
/// </summary>
public class SignedUpdateEndToEndTests
{
    private const string Running = "1.2.0";
    private const string Tag = "v1.2.1";
    private const string Asset = "realm-portal_1.2.1_amd64.deb";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("pretend this is a .deb");

    private static string Download(string name) =>
        $"https://github.com/KarloZ7715/realm-portal/releases/download/{Tag}/{name}";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            if (!Files.TryGetValue(url, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            if (url == GitHubReleaseFeed.LatestReleaseUri.ToString())
            {
                // GitHub always sends an ETag; the checker caches the release (and its asset list) only with one.
                response.Content.Headers.ContentType = new("application/json");
                response.Headers.ETag = new("\"release-etag\"");
            }
            return Task.FromResult(response);
        }
    }

    /// <summary>A throwaway minisign key pair that only lives for one test.</summary>
    private sealed class TestKey
    {
        private readonly Ed25519PrivateKeyParameters _secret = new(new SecureRandom());
        private readonly byte[] _keyId = RandomNumberGenerator.GetBytes(8);

        public MinisignPublicKey PublicKey
        {
            get
            {
                var blob = new byte[42];
                "Ed"u8.CopyTo(blob);
                _keyId.CopyTo(blob, 2);
                _secret.GeneratePublicKey().GetEncoded().CopyTo(blob, 10);
                return MinisignPublicKey.Parse(Convert.ToBase64String(blob));
            }
        }

        /// <summary>Same layout as <c>minisign -S</c> (prehashed): ED + key ID + Ed25519(BLAKE2b-512(file)), then the global signature.</summary>
        public byte[] Sign(byte[] content, string trustedComment)
        {
            var hash = new byte[64];
            var blake = new Blake2bDigest(512);
            blake.BlockUpdate(content, 0, content.Length);
            blake.DoFinal(hash, 0);
            var signature = Ed25519(hash);

            var blob = new byte[74];
            "ED"u8.CopyTo(blob);
            _keyId.CopyTo(blob, 2);
            signature.CopyTo(blob, 10);

            var global = Ed25519([.. signature, .. Encoding.UTF8.GetBytes(trustedComment)]);
            return Encoding.UTF8.GetBytes(
                "untrusted comment: test-only signature\n"
                + Convert.ToBase64String(blob) + "\n"
                + "trusted comment: " + trustedComment + "\n"
                + Convert.ToBase64String(global) + "\n");
        }

        private byte[] Ed25519(byte[] message)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, _secret);
            signer.BlockUpdate(message, 0, message.Length);
            return signer.GenerateSignature();
        }
    }

    private static byte[] Sums() =>
        Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant()}  {Asset}\n"
                               + $"{new string('0', 64)}  RealmPortal-1.2.1-x86_64.AppImage\n");

    private static FakeGitHub Release(byte[] sums, byte[]? signature, bool listSignature = true)
    {
        var assets = new List<object>
        {
            new { name = Asset, browser_download_url = Download(Asset), size = Payload.Length },
            new { name = "SHA256SUMS", browser_download_url = Download("SHA256SUMS"), size = sums.Length },
        };
        if (listSignature)
            assets.Add(new { name = ReleaseSignature.AssetName, browser_download_url = Download(ReleaseSignature.AssetName), size = signature?.Length ?? 300 });

        var github = new FakeGitHub();
        github.Files[GitHubReleaseFeed.LatestReleaseUri.ToString()] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = Tag,
            draft = false,
            prerelease = false,
            body = "notes",
            html_url = "https://github.com/KarloZ7715/realm-portal/releases/tag/" + Tag,
            assets,
        });
        github.Files[Download("SHA256SUMS")] = sums;
        github.Files[Download(Asset)] = Payload;
        if (signature is not null)
            github.Files[Download(ReleaseSignature.AssetName)] = signature;
        return github;
    }

    private sealed record Outcome(UpdateInstallResult? Result, UpdateException? Error, byte[][] Placed, List<string> Requested);

    private static async Task<Outcome> CheckAndInstall(FakeGitHub github, MinisignPublicKey trusted)
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var downloads = dir.Combine("downloads");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(downloads);
        var configLock = new ConfigDirectoryLock(config);
        var writer = new AtomicFileWriter();

        using var http = new HttpClient(github);
        using var feed = new GitHubReleaseFeed(http);
        using var downloader = new UpdateDownloader(
            http,
            installKind: () => InstallKind.Deb,
            appImageSelfReplace: () => false,
            downloadsDirectory: () => downloads,
            signatureVerifier: new MinisignVerifier([trusted]));
        var checker = new UpdateChecker(
            new NetworkSettingsStore(config, writer, configLock),
            new UpdateCacheStore(config, writer, configLock),
            feed,
            downloader,
            runningVersion: () => Running);

        var check = await checker.CheckAsync(UpdateCheckMode.Manual);
        Assert.Equal(UpdateCheckOutcome.Offer, check.Outcome);
        Assert.Equal(Tag, check.Offer!.TagName);

        UpdateInstallResult? result = null;
        UpdateException? error = null;
        try
        {
            result = await checker.InstallUpdateAsync(check.Offer);
        }
        catch (UpdateException ex)
        {
            error = ex;
        }

        var placed = Directory.GetFiles(downloads, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToArray();
        return new Outcome(result, error, placed, github.Requested);
    }

    private static void AssertRefused(Outcome outcome, UpdateErrorCode code, string bodyKey, SignatureStatus? status = null)
    {
        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.Error);
        Assert.Equal(code, outcome.Error.Code);
        if (status is not null)
            Assert.Equal(status, outcome.Error.Signature);
        Assert.Equal(bodyKey, UpdateInstallPresentation.FailureBodyKey(outcome.Error));
        Assert.Empty(outcome.Placed);
        Assert.DoesNotContain(Download(Asset), outcome.Requested);
    }

    [Fact]
    public async Task ValidSignature_DownloadIsAccepted()
    {
        var key = new TestKey();
        var sums = Sums();
        var outcome = await CheckAndInstall(Release(sums, key.Sign(sums, "realm-portal " + Tag)), key.PublicKey);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Result);
        Assert.Equal(InstallKind.Deb, outcome.Result.Kind);
        Assert.Equal(Payload, Assert.Single(outcome.Placed));
        Assert.Contains(Download(ReleaseSignature.AssetName), outcome.Requested);
    }

    [Fact]
    public async Task SignatureListedButNotThere_IsUnsigned()
    {
        var key = new TestKey();
        var outcome = await CheckAndInstall(Release(Sums(), signature: null), key.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureMissing, "update.install.unsigned.body");
        Assert.Contains(Download(ReleaseSignature.AssetName), outcome.Requested);
    }

    [Fact]
    public async Task SignatureNotInTheAssetList_IsUnsigned()
    {
        var key = new TestKey();
        var outcome = await CheckAndInstall(Release(Sums(), signature: null, listSignature: false), key.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureMissing, "update.install.unsigned.body");
        Assert.DoesNotContain(Download("SHA256SUMS"), outcome.Requested);
    }

    [Fact]
    public async Task TamperedSignature_IsRefused()
    {
        var key = new TestKey();
        var sums = Sums();
        var lines = Encoding.UTF8.GetString(key.Sign(sums, "realm-portal " + Tag)).Split('\n');
        var blob = Convert.FromBase64String(lines[1]);
        blob[20] ^= 0x01;
        lines[1] = Convert.ToBase64String(blob);
        var outcome = await CheckAndInstall(Release(sums, Encoding.UTF8.GetBytes(string.Join('\n', lines))), key.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureInvalid, "update.install.badsignature.body", SignatureStatus.BadSignature);
    }

    [Fact]
    public async Task TamperedSums_AreRefused()
    {
        var key = new TestKey();
        var sums = Sums();
        var signature = key.Sign(sums, "realm-portal " + Tag);
        var swapped = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(sums).Replace(new string('0', 64), new string('1', 64)));
        var outcome = await CheckAndInstall(Release(swapped, signature), key.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureInvalid, "update.install.badsignature.body", SignatureStatus.BadSignature);
    }

    [Fact]
    public async Task UnknownKey_IsRefused()
    {
        var trusted = new TestKey();
        var other = new TestKey();
        var sums = Sums();
        var outcome = await CheckAndInstall(Release(sums, other.Sign(sums, "realm-portal " + Tag)), trusted.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureInvalid, "update.install.badsignature.body", SignatureStatus.UnknownKey);
    }

    [Theory]
    [InlineData("realm-portal v1.2.0")]
    [InlineData("realm-portal v1.2.1-rc.1")]
    [InlineData("realm-portal v1.2.1 ")]
    public async Task TrustedCommentForAnotherTag_IsRefused(string comment)
    {
        var key = new TestKey();
        var sums = Sums();
        var outcome = await CheckAndInstall(Release(sums, key.Sign(sums, comment)), key.PublicKey);

        AssertRefused(outcome, UpdateErrorCode.SignatureInvalid, "update.install.badsignature.body", SignatureStatus.TrustedCommentMismatch);
    }
}
