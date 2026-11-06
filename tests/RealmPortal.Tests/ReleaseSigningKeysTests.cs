using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using RealmPortal.Core.Updates.Signing;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// The release key built into the app (#116) must be the one in minisign.pub at the repository root, which
/// packaging/sign-and-publish.sh verifies against.
/// </summary>
public class ReleaseSigningKeysTests
{
    private const string ReleaseKeyId = "0D03B1CC6600D915";
    private const string Tag = "realm-portal v1.2.0";

    [Fact]
    public void ForRelease_TrustsExactlyTheReleaseKey()
    {
        var verifier = MinisignVerifier.ForRelease();

        var key = Assert.Single(verifier.TrustedKeys);
        Assert.Equal(ReleaseKeyId, key.KeyId);
    }

    [Fact]
    public void EmbeddedKey_MatchesMinisignPubInTheRepository()
    {
        var path = FindMinisignPub();
        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain((byte)'\r', bytes);
        var lines = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
        Assert.Equal(3, lines.Length); // two lines, each ending in LF
        Assert.Equal(string.Empty, lines[2]);

        // Decode the key line directly instead of trusting MinisignPublicKey's parser or the comment.
        var raw = Convert.FromBase64String(lines[1]);
        Assert.Equal(2 + MinisignPublicKey.KeyIdLength + MinisignPublicKey.KeyLength, raw.Length);
        Assert.Equal((byte)'E', raw[0]);
        Assert.Equal((byte)'d', raw[1]);
        var keyId = raw[2..(2 + MinisignPublicKey.KeyIdLength)];
        var keyBytes = raw[(2 + MinisignPublicKey.KeyIdLength)..];
        var decodedKeyId = BinaryPrimitives.ReadUInt64LittleEndian(keyId).ToString("X16", CultureInfo.InvariantCulture);
        Assert.Equal(ReleaseKeyId, decodedKeyId);

        var embedded = Assert.Single(ReleaseSigningKeys.Trusted);
        Assert.Equal(keyId, embedded.KeyIdBytes.ToArray());
        Assert.Equal(keyBytes, embedded.KeyBytes.ToArray());

        // The comment is untrusted, but it must not name a different key.
        var comment = Regex.Match(lines[0], "^untrusted comment: minisign public key ([0-9A-F]{16})$");
        Assert.True(comment.Success, "Unexpected comment line in minisign.pub: " + lines[0]);
        Assert.Equal(decodedKeyId, comment.Groups[1].Value);
    }

    [Fact]
    public void ForRelease_RejectsAnUnknownKeyId()
    {
        var sums = "0000000000000000000000000000000000000000000000000000000000000000  RealmPortal-1.2.0-win-x64.zip\n"u8.ToArray();
        var (key, signature) = SyntheticSigner.Sign(sums, Tag);
        Assert.NotEqual(ReleaseKeyId, key.KeyId);

        Assert.Equal(SignatureStatus.UnknownKey, MinisignVerifier.ForRelease().Verify(sums, signature, Tag));
    }

    [Fact]
    public void ForRelease_RejectsTheRetiredKey()
    {
        // A71D9203F538E296 was replaced before any release was signed with it.
        const ulong retiredKeyIdValue = 0xA71D9203F538E296;
        var retiredKeyId = retiredKeyIdValue.ToString("X16", CultureInfo.InvariantCulture);
        Assert.DoesNotContain(MinisignVerifier.ForRelease().TrustedKeys, k => k.KeyId == retiredKeyId);

        // A signature carrying the retired key ID must be rejected.
        var sums = "0000000000000000000000000000000000000000000000000000000000000000  RealmPortal-1.2.0-win-x64.zip\n"u8.ToArray();
        var (_, signature) = SyntheticSigner.Sign(sums, Tag);
        var lines = System.Text.Encoding.UTF8.GetString(signature).Split('\n');
        var sig = Convert.FromBase64String(lines[1]);
        BinaryPrimitives.WriteUInt64LittleEndian(sig.AsSpan(2, 8), retiredKeyIdValue);
        lines[1] = Convert.ToBase64String(sig);
        var forged = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));

        Assert.Equal(SignatureStatus.UnknownKey, MinisignVerifier.ForRelease().Verify(sums, forged, Tag));
    }

    [Fact]
    public void ForRelease_DoesNotTrustTheTestKeys()
    {
        var fixtures = Path.Combine(RepositoryRoot(), "tests", "RealmPortal.Tests", "Fixtures", "Minisign");
        foreach (var name in new[] { "a", "b", "unknown" })
        {
            var testKey = MinisignPublicKey.Parse(File.ReadAllLines(Path.Combine(fixtures, $"test-key-{name}.pub"))[1]);
            Assert.DoesNotContain(ReleaseSigningKeys.Trusted, k => k.KeyId == testKey.KeyId);
        }

        var sums = File.ReadAllBytes(Path.Combine(fixtures, "SHA256SUMS"));
        var signedByTestKey = File.ReadAllBytes(Path.Combine(fixtures, "SHA256SUMS.a.minisig"));
        Assert.Equal(SignatureStatus.UnknownKey, MinisignVerifier.ForRelease().Verify(sums, signedByTestKey, Tag));
    }

    private static string FindMinisignPub()
    {
        var path = Path.Combine(RepositoryRoot(), "minisign.pub");
        if (!File.Exists(path))
            throw new FileNotFoundException("minisign.pub not found at the repository root: " + path, path);
        return path;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RealmPortal.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            "Repository root (RealmPortal.sln) not found above " + AppContext.BaseDirectory + "; cannot locate minisign.pub");
    }
}
