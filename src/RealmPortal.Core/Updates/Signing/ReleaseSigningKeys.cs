namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// Public keys trusted for release signatures, as <c>minisign.pub</c> base64 lines (key ID and key). See
/// docs/updates-and-status.md for how keys are added and rotated.
/// </summary>
public static class ReleaseSigningKeys
{
    // No release key has been generated yet, so the list is empty and every check returns NoTrustedKeys. The maintainer's
    // key is added here (and published as minisign.pub) in the PR that turns the check on. During a rotation the list holds
    // the old and the new key.
    //
    // A mistyped entry makes this type's static initializer throw, and a repeated key ID makes MinisignVerifier.ForRelease()
    // throw, both at the first update check. So the PR that adds a key must also replace the
    // BuiltInKeyList_IsEmptyUntilTheReleaseKeyExists test with one that builds MinisignVerifier.ForRelease() and checks
    // the key IDs against minisign.pub.
    private static readonly string[] Encoded = [];

    public static IReadOnlyList<MinisignPublicKey> Trusted { get; } =
        Encoded.Select(MinisignPublicKey.Parse).ToArray();
}
