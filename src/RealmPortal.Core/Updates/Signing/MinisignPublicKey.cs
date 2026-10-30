using System.Buffers.Binary;
using System.Globalization;

namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// A minisign public key: <c>base64("Ed" || key id (8 bytes) || Ed25519 public key (32 bytes))</c>, the second line of a
/// <c>minisign.pub</c> file.
/// </summary>
public sealed class MinisignPublicKey
{
    public const int KeyIdLength = 8;
    public const int KeyLength = 32;
    private const int EncodedLength = 56; // base64 of 2 + 8 + 32 bytes

    private readonly byte[] _keyId;
    private readonly byte[] _key;

    private MinisignPublicKey(byte[] keyId, byte[] key)
    {
        _keyId = keyId;
        _key = key;
    }

    internal ReadOnlySpan<byte> KeyIdBytes => _keyId;

    internal ReadOnlySpan<byte> KeyBytes => _key;

    /// <summary>Key ID as minisign prints it (16 upper-case hex digits).</summary>
    public string KeyId =>
        BinaryPrimitives.ReadUInt64LittleEndian(_keyId).ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>Parses the base64 line of a public key. Returns false for anything that is not exactly one key.</summary>
    public static bool TryParse(string? base64, out MinisignPublicKey? key)
    {
        key = null;
        if (base64 is null || base64.Length != EncodedLength)
            return false;
        if (!MinisignSignature.TryDecodeCanonicalBase64(base64, 2 + KeyIdLength + KeyLength, out var raw))
            return false;
        // Public keys always say "Ed", also for keys that make prehashed ("ED") signatures.
        if (raw[0] != (byte)'E' || raw[1] != (byte)'d')
            return false;
        key = new MinisignPublicKey(raw[2..(2 + KeyIdLength)], raw[(2 + KeyIdLength)..]);
        return true;
    }

    public static MinisignPublicKey Parse(string base64) =>
        TryParse(base64, out var key)
            ? key!
            : throw new FormatException("Not a minisign Ed25519 public key.");
}
