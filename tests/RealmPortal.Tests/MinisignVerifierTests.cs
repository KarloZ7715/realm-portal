using System.Text;
using RealmPortal.Core.Updates;
using RealmPortal.Core.Updates.Signing;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// Release signature checks (#116) against fixtures made with the real minisign CLI. The keys in Fixtures/Minisign are
/// test-only throwaway keys (see the README there).
/// </summary>
public class MinisignVerifierTests
{
    private const string Tag = "realm-portal v1.2.0";
    private static readonly string Dir = FindFixtures();

    private static readonly MinisignPublicKey KeyA = PublicKey("a");
    private static readonly MinisignPublicKey KeyB = PublicKey("b");
    private static readonly MinisignPublicKey KeyUnknown = PublicKey("unknown");

    private static byte[] Sums => Read("SHA256SUMS");

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        Assert.Equal(SignatureStatus.Valid, Verify([KeyA], Sums, Read("SHA256SUMS.a.minisig"), Tag));
    }

    [Fact]
    public void ReleaseCandidateTag_IsCheckedLikeAnyOther()
    {
        var sig = Read("SHA256SUMS.a-rc.minisig");
        Assert.Equal(SignatureStatus.Valid, Verify([KeyA], Sums, sig, ReleaseSignature.TrustedCommentFor("v1.2.0-rc.1")));
        Assert.Equal(SignatureStatus.TrustedCommentMismatch, Verify([KeyA], Sums, sig, Tag));
    }

    [Fact]
    public void TrustedComment_IsBuiltFromTheTag()
    {
        Assert.Equal(Tag, ReleaseSignature.TrustedCommentFor(new ReleaseVersion(1, 2, 0)));
        Assert.Equal("SHA256SUMS.minisig", ReleaseSignature.AssetName);
    }

    [Fact]
    public void SignatureFromAnotherKey_IsUnknownKey()
    {
        Assert.Equal(SignatureStatus.UnknownKey, Verify([KeyA], Sums, Read("SHA256SUMS.unknown.minisig"), Tag));
        Assert.Equal(SignatureStatus.UnknownKey, Verify([KeyA], Sums, Read("SHA256SUMS.b.minisig"), Tag));
    }

    [Fact]
    public void KeyIdOfATrustedKeyOnAForeignSignature_FailsTheSignatureCheck()
    {
        // Signed by the unknown key, but its key ID rewritten to key A's: the lookup succeeds, the math must not.
        var lines = Lines(Read("SHA256SUMS.unknown.minisig"));
        var blob = Convert.FromBase64String(lines[1]);
        Convert.FromBase64String(File.ReadAllLines(Path.Combine(Dir, "test-key-a.pub"))[1]).AsSpan(2, 8).CopyTo(blob.AsSpan(2, 8));
        lines[1] = Convert.ToBase64String(blob);
        Assert.Equal(SignatureStatus.BadSignature, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Fact]
    public void ModifiedFile_IsRejected()
    {
        var sums = Sums;
        sums[0] ^= 0x01;
        Assert.Equal(SignatureStatus.BadSignature, Verify([KeyA], sums, Read("SHA256SUMS.a.minisig"), Tag));

        var appended = Sums.Concat(Encoding.ASCII.GetBytes("0000  evil.AppImage\n")).ToArray();
        Assert.Equal(SignatureStatus.BadSignature, Verify([KeyA], appended, Read("SHA256SUMS.a.minisig"), Tag));
    }

    [Theory]
    [InlineData(10)] // first byte of the Ed25519 signature
    [InlineData(73)] // last byte
    public void ModifiedSignatureBytes_AreRejected(int offset)
    {
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        var blob = Convert.FromBase64String(lines[1]);
        blob[offset] ^= 0x80;
        lines[1] = Convert.ToBase64String(blob);
        Assert.Equal(SignatureStatus.BadSignature, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Fact]
    public void ModifiedGlobalSignature_IsRejected()
    {
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        var global = Convert.FromBase64String(lines[3]);
        global[5] ^= 0x01;
        lines[3] = Convert.ToBase64String(global);
        Assert.Equal(SignatureStatus.BadTrustedComment, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Fact]
    public void EditedTrustedComment_FailsTheGlobalSignature()
    {
        // An attacker who rewrites the tag to match the release being installed still cannot re-sign it.
        var lines = Lines(Read("SHA256SUMS-1.1.9.a.minisig"));
        lines[2] = "trusted comment: " + Tag;
        Assert.Equal(SignatureStatus.BadTrustedComment, Verify([KeyA], Read("SHA256SUMS-1.1.9"), Join(lines), Tag));
    }

    [Fact]
    public void WrongTag_IsTrustedCommentMismatch()
    {
        Assert.Equal(
            SignatureStatus.TrustedCommentMismatch,
            Verify([KeyA], Sums, Read("SHA256SUMS.a.minisig"), "realm-portal v1.2.1"));
        Assert.Equal(
            SignatureStatus.TrustedCommentMismatch,
            Verify([KeyA], Sums, Read("SHA256SUMS.a.minisig"), "realm-portal v1.2.0 "));
    }

    [Fact]
    public void ReplayedOlderRelease_IsTrustedCommentMismatch()
    {
        // A genuine, older SHA256SUMS and its signature served as if they were the new release.
        Assert.Equal(
            SignatureStatus.TrustedCommentMismatch,
            Verify([KeyA], Read("SHA256SUMS-1.1.9"), Read("SHA256SUMS-1.1.9.a.minisig"), Tag));
        // The older signature next to the new SHA256SUMS.
        Assert.Equal(
            SignatureStatus.BadSignature,
            Verify([KeyA], Sums, Read("SHA256SUMS-1.1.9.a.minisig"), Tag));
    }

    [Fact]
    public void LegacyEdSignature_IsRejected()
    {
        Assert.Equal(
            SignatureStatus.UnsupportedAlgorithm,
            Verify([KeyA], Sums, Read("SHA256SUMS.a-legacy.minisig"), Tag));
    }

    [Fact]
    public void UnknownAlgorithm_IsRejected()
    {
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        var blob = Convert.FromBase64String(lines[1]);
        blob[1] = (byte)'X';
        lines[1] = Convert.ToBase64String(blob);
        Assert.Equal(SignatureStatus.UnsupportedAlgorithm, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Fact]
    public void EmptyTrustedKeyList_VerifiesNothing()
    {
        Assert.Equal(SignatureStatus.NoTrustedKeys, Verify([], Sums, Read("SHA256SUMS.a.minisig"), Tag));
    }

    [Fact]
    public void Rotation_BothKeysAreAcceptedWhileBothAreTrusted()
    {
        MinisignPublicKey[] both = [KeyA, KeyB];
        Assert.Equal(SignatureStatus.Valid, Verify(both, Sums, Read("SHA256SUMS.a.minisig"), Tag));
        Assert.Equal(SignatureStatus.Valid, Verify(both, Sums, Read("SHA256SUMS.b.minisig"), Tag));
        // After the old key is dropped.
        Assert.Equal(SignatureStatus.UnknownKey, Verify([KeyB], Sums, Read("SHA256SUMS.a.minisig"), Tag));
        Assert.Equal(SignatureStatus.Valid, Verify([KeyB], Sums, Read("SHA256SUMS.b.minisig"), Tag));
    }

    [Fact]
    public void DuplicateKeyIds_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => new MinisignVerifier([KeyA, KeyB, KeyA]));
    }

    [Fact]
    public void KeyId_MatchesWhatMinisignPrints()
    {
        var header = File.ReadAllLines(Path.Combine(Dir, "test-key-a.pub"))[0];
        Assert.Equal("untrusted comment: minisign public key " + KeyA.KeyId, header);
        Assert.NotEqual(KeyA.KeyId, KeyUnknown.KeyId);
    }

    [Fact]
    public void LfAndCrlf_AreBothAccepted_WithOrWithoutFinalNewline()
    {
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        Assert.Equal(SignatureStatus.Valid, Verify([KeyA], Sums, Encoding.UTF8.GetBytes(string.Join("\n", lines)), Tag));
        Assert.Equal(SignatureStatus.Valid, Verify([KeyA], Sums, Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"), Tag));
    }

    public static TheoryData<string> MalformedEdits() => new()
    {
        "truncated",
        "empty",
        "three-lines",
        "trailing-line",
        "two-final-newlines",
        "no-untrusted-prefix",
        "no-trusted-prefix",
        "bad-base64-char",
        "base64-whitespace",
        "short-signature-line",
        "long-signature-line",
        "short-global",
        "noncanonical-padding",
        "lone-cr",
        "nul-in-comment",
        "invalid-utf8",
        "garbage",
        "long-untrusted-comment",
        "long-trusted-comment",
    };

    [Theory]
    [MemberData(nameof(MalformedEdits))]
    public void MalformedSignatureFiles_AreMalformed_AndNeverThrow(string edit)
    {
        var original = Read("SHA256SUMS.a.minisig");
        var text = Encoding.UTF8.GetString(original);
        var lines = Lines(original);
        byte[] file = edit switch
        {
            "truncated" => original[..(original.Length / 2)],
            "empty" => [],
            "three-lines" => Join(lines[..3]),
            "trailing-line" => Encoding.UTF8.GetBytes(text + "extra\n"),
            "two-final-newlines" => Encoding.UTF8.GetBytes(text + "\n"),
            "no-untrusted-prefix" => Join(["comment: x", lines[1], lines[2], lines[3]]),
            "no-trusted-prefix" => Join([lines[0], lines[1], "comment: " + Tag, lines[3]]),
            "bad-base64-char" => Join([lines[0], "*" + lines[1][1..], lines[2], lines[3]]),
            "base64-whitespace" => Join([lines[0], lines[1][..50] + " " + lines[1][51..], lines[2], lines[3]]),
            "short-signature-line" => Join([lines[0], lines[1][..96], lines[2], lines[3]]),
            "long-signature-line" => Join([lines[0], lines[1] + "AAAA", lines[2], lines[3]]),
            "short-global" => Join([lines[0], lines[1], lines[2], lines[3][..84]]),
            "noncanonical-padding" => Join([lines[0], NonCanonical(lines[1]), lines[2], lines[3]]),
            "lone-cr" => Join([lines[0], lines[1], lines[2][..20] + "\r" + lines[2][20..], lines[3]]),
            "nul-in-comment" => Join([lines[0] + "\0", lines[1], lines[2], lines[3]]),
            "invalid-utf8" => [.. original[..20], 0xFF, 0xFE, .. original[20..]],
            "garbage" => Encoding.ASCII.GetBytes("not a signature at all"),
            "long-untrusted-comment" => Join(["untrusted comment: " + new string('x', 1004), lines[1], lines[2], lines[3]]),
            "long-trusted-comment" => Join([lines[0], lines[1], "trusted comment: " + new string('x', 8174), lines[3]]),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };

        Assert.Equal(SignatureStatus.Malformed, Verify([KeyA], Sums, file, Tag));
    }

    [Fact]
    public void OversizedInputs_AreTooLarge()
    {
        var bigSig = new byte[MinisignSignature.MaxFileBytes + 1];
        Assert.Equal(SignatureStatus.TooLarge, Verify([KeyA], Sums, bigSig, Tag));

        var bigContent = new byte[MinisignVerifier.MaxSignedContentBytes + 1];
        Assert.Equal(SignatureStatus.TooLarge, Verify([KeyA], bigContent, Read("SHA256SUMS.a.minisig"), Tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingExpectedComment_IsACallerBug(string? expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => Verify([KeyA], Sums, Read("SHA256SUMS.a.minisig"), expected!));
        Assert.Equal("expectedTrustedComment", ex.ParamName);

        // Also through the interface, and before any key or input check.
        IReleaseSignatureVerifier none = new MinisignVerifier([]);
        Assert.Throws<ArgumentException>(() => none.Verify([], [], expected!));
    }

    [Theory]
    [InlineData(1003, SignatureStatus.Valid)]
    [InlineData(1004, SignatureStatus.Malformed)]
    public void UntrustedComment_LimitMatchesMinisign(int length, SignatureStatus expected)
    {
        // The untrusted comment is not signed, so the fixture stays valid apart from the length.
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        lines[0] = "untrusted comment: " + new string('u', length);
        Assert.Equal(expected, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Fact]
    public void UntrustedComment_LimitCountsUtf8Bytes()
    {
        var lines = Lines(Read("SHA256SUMS.a.minisig"));
        lines[0] = "untrusted comment: " + new string('u', 1001) + "\u00e9"; // 1003 bytes
        Assert.Equal(SignatureStatus.Valid, Verify([KeyA], Sums, Join(lines), Tag));
        lines[0] = "untrusted comment: " + new string('u', 1002) + "\u00e9"; // 1004 bytes
        Assert.Equal(SignatureStatus.Malformed, Verify([KeyA], Sums, Join(lines), Tag));
    }

    [Theory]
    [InlineData(4077)] // longest that `minisign -S` writes
    [InlineData(8173)] // longest that minisign reads
    public void TrustedComment_UpToTheReadLimit_IsAccepted(int length)
    {
        // minisign cannot write a trusted comment this long, so the signature is made here with a throwaway key.
        var comment = Tag + " " + new string('t', length - Tag.Length - 1);
        var (key, sig) = SyntheticSigner.Sign(Sums, comment);
        Assert.Equal(SignatureStatus.Valid, Verify([key], Sums, sig, comment));
    }

    [Fact]
    public void TrustedComment_OneByteOverTheReadLimit_IsMalformed()
    {
        var comment = Tag + " " + new string('t', 8174 - Tag.Length - 1);
        var (key, sig) = SyntheticSigner.Sign(Sums, comment);
        Assert.Equal(SignatureStatus.Malformed, Verify([key], Sums, sig, comment));
    }

    [Fact]
    public void SyntheticSigner_ProducesSignaturesTheVerifierAccepts()
    {
        // Guards the helper above on a normal comment.
        var (key, sig) = SyntheticSigner.Sign(Sums, Tag);
        Assert.Equal(SignatureStatus.Valid, Verify([key], Sums, sig, Tag));
        Assert.Equal(SignatureStatus.UnknownKey, Verify([KeyA], Sums, sig, Tag));
        var tampered = Sums;
        tampered[0] ^= 1;
        Assert.Equal(SignatureStatus.BadSignature, Verify([key], tampered, sig, Tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("RWRHtd4HeZbgFvEgAD9WDPiG0Xdd9Nyff96odIky9jnzSTb/a8nIQ/U")] // one character short
    [InlineData("RWRHtd4HeZbgFvEgAD9WDPiG0Xdd9Nyff96odIky9jnzSTb/a8nIQ/Um=")]
    [InlineData("REQHtd4HeZbgFvEgAD9WDPiG0Xdd9Nyff96odIky9jnzSTb/a8nIQ/Um")] // "ED" instead of "Ed"
    [InlineData(" RWRHtd4HeZbgFvEgAD9WDPiG0Xdd9Nyff96odIky9jnzSTb/a8nIQ/U")]
    public void PublicKeyParser_RejectsAnythingButOneKey(string text)
    {
        Assert.False(MinisignPublicKey.TryParse(text, out _));
        Assert.Throws<FormatException>(() => MinisignPublicKey.Parse(text));
    }

    private static SignatureStatus Verify(MinisignPublicKey[] keys, byte[] content, byte[] signature, string expected) =>
        new MinisignVerifier(keys).Verify(content, signature, expected);

    private static MinisignPublicKey PublicKey(string name) =>
        MinisignPublicKey.Parse(File.ReadAllLines(Path.Combine(Dir, $"test-key-{name}.pub"))[1]);

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Dir, name));

    private static string[] Lines(byte[] file) =>
        Encoding.UTF8.GetString(file).TrimEnd('\n').Split('\n');

    private static byte[] Join(string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");

    // 74 bytes encode to 100 characters ending in one "="; the last data character carries 2 unused bits.
    private static string NonCanonical(string line)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var i = line.Length - 2;
        var value = alphabet.IndexOf(line[i], StringComparison.Ordinal);
        return line[..i] + alphabet[value ^ 0x01] + line[(i + 1)..];
    }

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
