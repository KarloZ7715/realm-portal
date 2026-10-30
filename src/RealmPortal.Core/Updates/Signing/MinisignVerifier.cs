using System.Security.Cryptography;
using System.Text;

namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// Verifies prehashed minisign signatures (<c>ED</c>: Ed25519 over the BLAKE2b-512 hash of the file) against a fixed list of
/// trusted keys. Legacy <c>Ed</c> signatures over the raw file are rejected. See docs/updates-and-status.md.
/// </summary>
public sealed class MinisignVerifier : IReleaseSignatureVerifier
{
    /// <summary>Upper bound for the signed content. <c>SHA256SUMS</c> is read with a 64 KiB cap, so this is generous.</summary>
    public const int MaxSignedContentBytes = 1024 * 1024;

    private readonly MinisignPublicKey[] _trustedKeys;

    public MinisignVerifier(IEnumerable<MinisignPublicKey> trustedKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);
        _trustedKeys = trustedKeys.ToArray();
        for (var i = 0; i < _trustedKeys.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(_trustedKeys[i], nameof(trustedKeys));
            for (var j = 0; j < i; j++)
            {
                if (_trustedKeys[i].KeyIdBytes.SequenceEqual(_trustedKeys[j].KeyIdBytes))
                    throw new ArgumentException($"Duplicate trusted key ID {_trustedKeys[i].KeyId}.", nameof(trustedKeys));
            }
        }
    }

    /// <summary>The keys this verifier trusts, in order.</summary>
    internal IReadOnlyList<MinisignPublicKey> TrustedKeys => _trustedKeys;

    /// <summary>A verifier for the keys built into this app (<see cref="ReleaseSigningKeys.Trusted"/>).</summary>
    public static MinisignVerifier ForRelease() => new(ReleaseSigningKeys.Trusted);

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="expectedTrustedComment"/> is null or empty.</exception>
    public SignatureStatus Verify(
        ReadOnlySpan<byte> signedContent,
        ReadOnlySpan<byte> signatureFile,
        string expectedTrustedComment)
    {
        if (string.IsNullOrEmpty(expectedTrustedComment))
            throw new ArgumentException("An expected trusted comment is required.", nameof(expectedTrustedComment));

        if (_trustedKeys.Length == 0)
            return SignatureStatus.NoTrustedKeys;
        if (signedContent.Length > MaxSignedContentBytes)
            return SignatureStatus.TooLarge;

        var parsed = MinisignSignature.TryParse(signatureFile, out var sig);
        if (parsed != SignatureStatus.Valid)
            return parsed;
        if (!sig!.IsPrehashedEd25519)
            return SignatureStatus.UnsupportedAlgorithm;

        var key = FindKey(sig.KeyId);
        if (key is null)
            return SignatureStatus.UnknownKey;

        Span<byte> hash = stackalloc byte[64];
        BouncyCastleEd25519.Blake2b512(signedContent, hash);
        if (!BouncyCastleEd25519.Verify(key.KeyBytes, hash, sig.Signature))
            return SignatureStatus.BadSignature;

        var global = new byte[sig.Signature.Length + sig.TrustedComment.Length];
        sig.Signature.CopyTo(global, 0);
        sig.TrustedComment.CopyTo(global, sig.Signature.Length);
        if (!BouncyCastleEd25519.Verify(key.KeyBytes, global, sig.GlobalSignature))
            return SignatureStatus.BadTrustedComment;

        var expected = Encoding.UTF8.GetBytes(expectedTrustedComment);
        if (expected.Length != sig.TrustedComment.Length
            || !CryptographicOperations.FixedTimeEquals(expected, sig.TrustedComment))
        {
            return SignatureStatus.TrustedCommentMismatch;
        }

        return SignatureStatus.Valid;
    }

    private MinisignPublicKey? FindKey(ReadOnlySpan<byte> keyId)
    {
        MinisignPublicKey? found = null;
        foreach (var key in _trustedKeys)
        {
            if (CryptographicOperations.FixedTimeEquals(key.KeyIdBytes, keyId))
                found = key;
        }

        return found;
    }
}
