using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// The only place that touches BouncyCastle. .NET 9 has no Ed25519 or BLAKE2b in the BCL; swapping the library (or moving to
/// the ECDSA fallback) only changes this file and <see cref="MinisignVerifier"/>.
/// </summary>
internal static class BouncyCastleEd25519
{
    public static void Blake2b512(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (destination.Length != 64)
            throw new ArgumentException("BLAKE2b-512 needs a 64-byte destination.", nameof(destination));
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(data);
        digest.DoFinal(destination);
    }

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != Ed25519.PublicKeySize || signature.Length != Ed25519.SignatureSize)
            return false;
        var msg = message.ToArray();
        return Ed25519.Verify(signature.ToArray(), 0, publicKey.ToArray(), 0, msg, 0, msg.Length);
    }
}
