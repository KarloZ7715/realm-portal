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
        "RWQV2QBmzLEDDRz6sZBwByp5xDjn0Cg2QgVTegnK4ZhbLv+pOGSXAQS8", // key ID 0D03B1CC6600D915
    ];

    public static IReadOnlyList<MinisignPublicKey> Trusted { get; } =
        Encoded.Select(MinisignPublicKey.Parse).ToArray();
}
