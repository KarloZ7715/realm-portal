using System.Text;

namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// Strict parser for the minisign signature file format:
/// <code>
/// untrusted comment: &lt;text&gt;
/// base64(algorithm (2 bytes) || key id (8 bytes) || signature (64 bytes))
/// trusted comment: &lt;text&gt;
/// base64(global signature (64 bytes))
/// </code>
/// Lines end with LF (CRLF is accepted). Nothing may follow the fourth line except its line ending.
/// </summary>
internal sealed class MinisignSignature
{
    public const int MaxFileBytes = 16 * 1024;
    public const int SignatureLength = 64;

    // Upper bounds on the comment text after its prefix. They are looser than minisign 0.12, which counts the prefix and
    // line ending inside fixed buffers: it reads untrusted comments of at most 1003 bytes (1024-byte line buffer) and
    // trusted comments of at most 8173 (8192), and `minisign -S` writes trusted comments of at most 4077 bytes (4096-byte
    // output buffer). Anything minisign produces fits here; the extra room only accepts hand-made files, whose trusted
    // comment is still covered by the global signature.
    private const int MaxUntrustedCommentBytes = 1024;
    private const int MaxTrustedCommentBytes = 8192;

    private const string UntrustedPrefix = "untrusted comment: ";
    private const string TrustedPrefix = "trusted comment: ";

    private MinisignSignature(byte[] algorithm, byte[] keyId, byte[] signature, byte[] trustedComment, byte[] globalSignature)
    {
        Algorithm = algorithm;
        KeyId = keyId;
        Signature = signature;
        TrustedComment = trustedComment;
        GlobalSignature = globalSignature;
    }

    public byte[] Algorithm { get; }
    public byte[] KeyId { get; }
    public byte[] Signature { get; }

    /// <summary>UTF-8 bytes of the trusted comment, without the prefix and line ending.</summary>
    public byte[] TrustedComment { get; }

    public byte[] GlobalSignature { get; }

    public bool IsPrehashedEd25519 => Algorithm[0] == (byte)'E' && Algorithm[1] == (byte)'D';

    public static SignatureStatus TryParse(ReadOnlySpan<byte> file, out MinisignSignature? signature)
    {
        signature = null;
        if (file.Length > MaxFileBytes)
            return SignatureStatus.TooLarge;

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return SignatureStatus.Malformed;
        }

        var lines = text.Split('\n');
        // Four lines, then either nothing (no final newline) or one empty piece after the final newline.
        if (lines.Length < 4 || lines.Length > 5 || (lines.Length == 5 && lines[4].Length != 0))
            return SignatureStatus.Malformed;
        for (var i = 0; i < 4; i++)
        {
            if (lines[i].EndsWith('\r'))
                lines[i] = lines[i][..^1];
            if (lines[i].Contains('\r', StringComparison.Ordinal) || lines[i].Contains('\0', StringComparison.Ordinal))
                return SignatureStatus.Malformed;
        }

        if (!lines[0].StartsWith(UntrustedPrefix, StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(lines[0]) - UntrustedPrefix.Length > MaxUntrustedCommentBytes)
        {
            return SignatureStatus.Malformed;
        }

        if (!TryDecodeCanonicalBase64(lines[1], 2 + MinisignPublicKey.KeyIdLength + SignatureLength, out var sigBlob))
            return SignatureStatus.Malformed;

        if (!lines[2].StartsWith(TrustedPrefix, StringComparison.Ordinal))
            return SignatureStatus.Malformed;
        var trusted = Encoding.UTF8.GetBytes(lines[2][TrustedPrefix.Length..]);
        if (trusted.Length > MaxTrustedCommentBytes)
            return SignatureStatus.Malformed;

        if (!TryDecodeCanonicalBase64(lines[3], SignatureLength, out var global))
            return SignatureStatus.Malformed;

        signature = new MinisignSignature(
            algorithm: sigBlob[..2],
            keyId: sigBlob[2..(2 + MinisignPublicKey.KeyIdLength)],
            signature: sigBlob[(2 + MinisignPublicKey.KeyIdLength)..],
            trustedComment: trusted,
            globalSignature: global);
        return SignatureStatus.Valid;
    }

    /// <summary>
    /// Decodes standard padded base64 that must round-trip exactly: no whitespace, no alternative padding bits, and exactly
    /// <paramref name="length"/> bytes.
    /// </summary>
    internal static bool TryDecodeCanonicalBase64(string text, int length, out byte[] bytes)
    {
        bytes = [];
        var encodedLength = (length + 2) / 3 * 4;
        if (text.Length != encodedLength)
            return false;
        foreach (var c in text)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=';
            if (!ok)
                return false;
        }

        var buffer = new byte[length + 2];
        if (!Convert.TryFromBase64String(text, buffer, out var written) || written != length)
            return false;
        var decoded = buffer[..length];
        if (!string.Equals(Convert.ToBase64String(decoded), text, StringComparison.Ordinal))
            return false;
        bytes = decoded;
        return true;
    }
}
