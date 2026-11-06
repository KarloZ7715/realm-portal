namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// Public keys trusted for release signatures, as <c>minisign.pub</c> base64 lines (key ID and key). See
/// docs/updates-and-status.md for how keys are added and rotated.
/// </summary>
public static class ReleaseSigningKeys
{
    // The base64 key lines of minisign.pub at the repository root, copied from that file. ReleaseSigningKeysTests checks
    // that they match it. During a rotation the list holds the old and the new key.
    //
    // A mistyped entry makes this type's static initializer throw, and a repeated key ID makes MinisignVerifier.ForRelease()
    // throw, both at the first update check; ReleaseSigningKeysTests catches both.
    private static readonly string[] Encoded =
    [
        "RWSW4jj1A5Idp08WKXT16AwD21ICuzdEz0CoIx39Wji6Zsffoqkq96Sl", // key ID A71D9203F538E296
    ];

    public static IReadOnlyList<MinisignPublicKey> Trusted { get; } =
        Encoded.Select(MinisignPublicKey.Parse).ToArray();
}
