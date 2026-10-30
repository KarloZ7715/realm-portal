using System.Security.Cryptography;
using System.Text;
using RealmPortal.Core.Updates.Signing;
using Ed25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Writes minisign <c>ED</c> signature files with a fresh throwaway key, for cases the minisign CLI cannot produce (for
/// example trusted comments longer than it writes). Test only; the key exists for one call.
/// </summary>
internal static class SyntheticSigner
{
    public static (MinisignPublicKey Key, byte[] SignatureFile) Sign(byte[] content, string trustedComment)
    {
        var secret = RandomNumberGenerator.GetBytes(Ed25519.SecretKeySize);
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(secret, 0, publicKey, 0);
        var keyId = RandomNumberGenerator.GetBytes(8);

        Span<byte> hash = stackalloc byte[64];
        BouncyCastleEd25519.Blake2b512(content, hash);
        var signature = SignBytes(secret, hash.ToArray());

        var comment = Encoding.UTF8.GetBytes(trustedComment);
        var global = SignBytes(secret, [.. signature, .. comment]);

        byte[] blob = [(byte)'E', (byte)'D', .. keyId, .. signature];
        var file = "untrusted comment: synthetic test signature\n"
            + Convert.ToBase64String(blob) + "\n"
            + "trusted comment: " + trustedComment + "\n"
            + Convert.ToBase64String(global) + "\n";

        var key = MinisignPublicKey.Parse(Convert.ToBase64String([(byte)'E', (byte)'d', .. keyId, .. publicKey]));
        return (key, Encoding.UTF8.GetBytes(file));
    }

    private static byte[] SignBytes(byte[] secret, byte[] message)
    {
        var sig = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(secret, 0, message, 0, message.Length, sig, 0);
        return sig;
    }
}
