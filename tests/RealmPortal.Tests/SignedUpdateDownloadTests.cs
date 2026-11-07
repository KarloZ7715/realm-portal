using System.Net;
using System.Text;
using RealmPortal.Core.Updates;
using RealmPortal.Core.Updates.Signing;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// The updater only trusts SHA256SUMS after its minisign signature checks out (#116). Uses the test-only keys and
/// signatures in Fixtures/Minisign; the release key is never involved.
/// </summary>
public class SignedUpdateDownloadTests
{
    private const string Asset = "realm-portal_1.2.0_amd64.deb";
    private static readonly string Dir = FindFixtures();
    private static readonly ReleaseVersion Version = new(1, 2, 0);
    private static readonly MinisignVerifier TestVerifier = new([Key("a")]);

    // make-fixtures.sh hashes this placeholder string for each package name.
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("test fixture " + Asset);

    private static string Url(string name) => ReleaseDocumentParser.ExpectedDownloadUrl(Version, name);

    private sealed class Server : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(Routes.TryGetValue(url, out var make)
                ? make()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Ok(byte[] body, bool withLength = true)
    {
        var content = new ByteArrayContent(body);
        if (!withLength)
            content = new ByteArrayContent(body) { Headers = { ContentLength = null } };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static Server SignedRelease(string signatureFixture = "SHA256SUMS.a.minisig")
    {
        var server = new Server();
        server.Routes[Url("SHA256SUMS")] = () => Ok(Read("SHA256SUMS"));
        server.Routes[Url(ReleaseSignature.AssetName)] = () => Ok(Read(signatureFixture));
        server.Routes[Url(Asset)] = () => Ok(Payload);
        return server;
    }

    private static ReleaseDocument Release(string tag = "v1.2.0", bool listSignature = true)
    {
        var assets = new List<ReleaseAsset>
        {
            new(Asset, Url(Asset), Payload.Length),
            new("SHA256SUMS", Url("SHA256SUMS"), 0),
        };
        if (listSignature)
            assets.Add(new ReleaseAsset(ReleaseSignature.AssetName, Url(ReleaseSignature.AssetName), 0));
        return new ReleaseDocument(Version, tag, "notes", null, assets);
    }

    private static async Task<(UpdateException? Error, UpdateInstallResult? Result, byte[][] Placed)> Install(
        Server server, ReleaseDocument release, IReleaseSignatureVerifier? verifier = null)
    {
        using var client = new HttpClient(server);
        using var dir = new TempDirectory();
        var downloads = dir.Combine("dl");
        Directory.CreateDirectory(downloads);
        using var dl = new UpdateDownloader(
            client,
            installKind: () => InstallKind.Deb,
            appImageSelfReplace: () => false,
            downloadsDirectory: () => downloads,
            signatureVerifier: verifier ?? TestVerifier);
        try
        {
            var result = await dl.DownloadAndPlaceAsync(release);
            return (null, result, Directory.GetFiles(downloads).Select(File.ReadAllBytes).ToArray());
        }
        catch (UpdateException ex)
        {
            return (ex, null, Directory.GetFiles(downloads).Select(File.ReadAllBytes).ToArray());
        }
    }

    [Fact]
    public async Task SignedRelease_IsVerifiedAndPlaced()
    {
        var server = SignedRelease();
        var (error, result, placed) = await Install(server, Release());

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Single(placed);
        Assert.Equal(Payload, placed[0]);
        Assert.Contains(Url(ReleaseSignature.AssetName), server.Requested);
    }

    [Fact]
    public async Task SignatureNotListedOnTheRelease_IsRefusedBeforeDownloading()
    {
        var server = SignedRelease();
        var (error, _, placed) = await Install(server, Release(listSignature: false));

        Assert.Equal(UpdateErrorCode.SignatureMissing, error?.Code);
        Assert.Empty(placed);
        Assert.Empty(server.Requested);
    }

    [Fact]
    public async Task SignatureAsset404_IsMissing_AndNothingIsDownloaded()
    {
        var server = SignedRelease();
        server.Routes.Remove(Url(ReleaseSignature.AssetName));
        var (error, _, placed) = await Install(server, Release());

        Assert.Equal(UpdateErrorCode.SignatureMissing, error?.Code);
        Assert.Empty(placed);
        Assert.DoesNotContain(Url(Asset), server.Requested);
    }

    [Fact]
    public async Task SignatureDownloadFailure_FailsClosed()
    {
        var server = SignedRelease();
        server.Routes[Url(ReleaseSignature.AssetName)] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (error, _, placed) = await Install(server, Release());

        Assert.NotNull(error);
        Assert.Equal(500, error.HttpStatus);
        Assert.Empty(placed);
        Assert.DoesNotContain(Url(Asset), server.Requested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedSignature_IsRefused(bool withLength)
    {
        var server = SignedRelease();
        var big = new byte[SignedSha256Sums.MaxSignatureBytes + 1];
        server.Routes[Url(ReleaseSignature.AssetName)] = () => Ok(big, withLength);
        var (error, _, placed) = await Install(server, Release());

        Assert.Equal(UpdateErrorCode.SignatureInvalid, error?.Code);
        Assert.Equal(SignatureStatus.TooLarge, error?.Signature);
        Assert.Empty(placed);
    }

    [Theory]
    [InlineData("SHA256SUMS.unknown.minisig", SignatureStatus.UnknownKey)]
    [InlineData("SHA256SUMS.b.minisig", SignatureStatus.UnknownKey)]
    [InlineData("SHA256SUMS.a-legacy.minisig", SignatureStatus.UnsupportedAlgorithm)]
    [InlineData("SHA256SUMS.a-rc.minisig", SignatureStatus.TrustedCommentMismatch)]
    [InlineData("SHA256SUMS-1.1.9.a.minisig", SignatureStatus.BadSignature)]
    public async Task BadSignatures_AreRefused(string fixture, SignatureStatus expected)
    {
        var (error, _, placed) = await Install(SignedRelease(fixture), Release());

        Assert.Equal(UpdateErrorCode.SignatureInvalid, error?.Code);
        Assert.Equal(expected, error?.Signature);
        Assert.Empty(placed);
    }

    [Fact]
    public async Task TamperedSums_AreRefused()
    {
        var server = SignedRelease();
        var sums = Read("SHA256SUMS");
        sums[0] ^= 0x01;
        server.Routes[Url("SHA256SUMS")] = () => Ok(sums);
        var (error, _, placed) = await Install(server, Release());

        Assert.Equal(SignatureStatus.BadSignature, error?.Signature);
        Assert.Empty(placed);
    }

    [Fact]
    public async Task MalformedSignature_IsRefused()
    {
        var server = SignedRelease();
        server.Routes[Url(ReleaseSignature.AssetName)] = () => Ok("not a signature\n"u8.ToArray());
        var (error, _, _) = await Install(server, Release());

        Assert.Equal(UpdateErrorCode.SignatureInvalid, error?.Code);
        Assert.Equal(SignatureStatus.Malformed, error?.Signature);
    }

    [Fact]
    public async Task TrustedComment_UsesTheRawTag()
    {
        // The rc signature only verifies when the expected comment keeps the "-rc.1" suffix.
        var (error, result, _) = await Install(SignedRelease("SHA256SUMS.a-rc.minisig"), Release(tag: "v1.2.0-rc.1"));
        Assert.Null(error);
        Assert.NotNull(result);

        // And the final signature is not accepted for the rc tag.
        var (wrong, _, _) = await Install(SignedRelease(), Release(tag: "v1.2.0-rc.1"));
        Assert.Equal(SignatureStatus.TrustedCommentMismatch, wrong?.Signature);
    }

    [Fact]
    public async Task ReleaseKey_DoesNotTrustTheTestKey()
    {
        var (error, _, placed) = await Install(SignedRelease(), Release(), MinisignVerifier.ForRelease());

        Assert.Equal(SignatureStatus.UnknownKey, error?.Signature);
        Assert.Empty(placed);
    }

    [Fact]
    public void Check_RejectsOversizedInputWithoutParsing()
    {
        var tooBig = new byte[SignedSha256Sums.MaxSignatureBytes + 1];
        Assert.Equal(SignatureStatus.TooLarge, SignedSha256Sums.Check(TestVerifier, "v1.2.0", Read("SHA256SUMS"), tooBig));
        Assert.Equal(SignatureStatus.TooLarge,
            SignedSha256Sums.Check(TestVerifier, "v1.2.0", new byte[Sha256Sums.MaxBytes + 1], Read("SHA256SUMS.a.minisig")));
        Assert.Equal(SignatureStatus.Valid,
            SignedSha256Sums.Check(TestVerifier, "v1.2.0", Read("SHA256SUMS"), Read("SHA256SUMS.a.minisig")));
    }

    [Fact]
    public void Printable_EscapesControlCharacters()
    {
        Assert.Equal("realm-portal v1.2.0", SignedSha256Sums.Printable("realm-portal v1.2.0"));
        Assert.Equal("a\\u000ab\\u001b[31m\\u202e", SignedSha256Sums.Printable("a\nb\u001b[31m\u202e"));
        Assert.Equal(201, SignedSha256Sums.Printable(new string('x', 500)).Length);
    }

    /// <summary>A non-seekable body handed out 1 KiB at a time, counting what the downloader actually pulled.</summary>
    private sealed class CountingStream(int size) : Stream
    {
        public const int Chunk = 1024;
        private readonly byte[] _data = new byte[size];

        public int Consumed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Consumed; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = Math.Min(Math.Min(buffer.Length, Chunk), _data.Length - Consumed);
            _data.AsMemory(Consumed, n).CopyTo(buffer);
            Consumed += n;
            return ValueTask.FromResult(n);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("SHA256SUMS", false)]
    [InlineData("SHA256SUMS", true)]
    [InlineData(ReleaseSignature.AssetName, false)]
    [InlineData(ReleaseSignature.AssetName, true)]
    public async Task OversizedStream_IsCutOffWhileReading(string name, bool smallContentLength)
    {
        // 256 KiB, sent chunked (no length) or with a Content-Length of 1 that lies about the size.
        var body = new CountingStream(256 * 1024);
        var server = SignedRelease();
        server.Routes[Url(name)] = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            if (smallContentLength)
                response.Content.Headers.ContentLength = 1;
            else
                response.Headers.TransferEncodingChunked = true;
            return response;
        };

        var (error, _, placed) = await Install(server, Release());

        var isSums = name == "SHA256SUMS";
        Assert.Equal(isSums ? UpdateErrorCode.DownloadTooLarge : UpdateErrorCode.SignatureInvalid, error?.Code);
        Assert.Empty(placed);
        var limit = isSums ? Sha256Sums.MaxBytes : SignedSha256Sums.MaxSignatureBytes;
        Assert.True(body.Consumed <= limit + CountingStream.Chunk,
            $"read {body.Consumed} bytes of {name} before giving up; the limit is {limit}");
    }

    private static MinisignPublicKey Key(string name) =>
        MinisignPublicKey.Parse(File.ReadAllLines(Path.Combine(Dir, $"test-key-{name}.pub"))[1]);

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Dir, name));

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RealmPortal.sln")))
                return Path.Combine(dir.FullName, "tests", "RealmPortal.Tests", "Fixtures", "Minisign");
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
