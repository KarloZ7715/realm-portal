namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// Checks the detached signature of a release's <c>SHA256SUMS</c> against the keys built into the app (#116).
/// A bad, damaged or hostile signature or content is reported as a <see cref="SignatureStatus"/>, never as an exception.
/// Only caller mistakes throw: a null or empty <c>expectedTrustedComment</c> throws
/// <see cref="ArgumentException"/>.
/// </summary>
public interface IReleaseSignatureVerifier
{
    /// <param name="signedContent">The exact bytes that were signed (the downloaded <c>SHA256SUMS</c>).</param>
    /// <param name="signatureFile">The raw signature file (<c>SHA256SUMS.minisig</c>).</param>
    /// <param name="expectedTrustedComment">
    /// The trusted comment the release must carry, from <see cref="ReleaseSignature.TrustedCommentFor(string)"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expectedTrustedComment"/> is null or empty.</exception>
    SignatureStatus Verify(
        ReadOnlySpan<byte> signedContent,
        ReadOnlySpan<byte> signatureFile,
        string expectedTrustedComment);
}

/// <summary>Outcome of a release signature check. Anything other than <see cref="Valid"/> means "do not install".</summary>
public enum SignatureStatus
{
    Valid = 0,

    /// <summary>The app has no trusted keys, so nothing can be verified.</summary>
    NoTrustedKeys,

    /// <summary>The signed content or the signature file is over its size limit.</summary>
    TooLarge,

    /// <summary>The signature file is not a well-formed minisign signature.</summary>
    Malformed,

    /// <summary>The signature uses an algorithm other than prehashed Ed25519 (<c>ED</c>), e.g. legacy <c>Ed</c>.</summary>
    UnsupportedAlgorithm,

    /// <summary>The signature's key ID is not in the trusted list.</summary>
    UnknownKey,

    /// <summary>The signature over the content does not verify (modified file, wrong key, damaged signature).</summary>
    BadSignature,

    /// <summary>The global signature over the signature and trusted comment does not verify.</summary>
    BadTrustedComment,

    /// <summary>The trusted comment is authentic but names a different release (e.g. an older one being replayed).</summary>
    TrustedCommentMismatch,
}

/// <summary>Naming rules shared by the release process and the client.</summary>
public static class ReleaseSignature
{
    /// <summary>Asset name of the detached signature over <c>SHA256SUMS</c>.</summary>
    public const string AssetName = "SHA256SUMS.minisig";

    /// <summary>
    /// Trusted comment for a release tag, e.g. <c>realm-portal v1.2.0</c> or <c>realm-portal v1.2.0-rc.1</c>.
    /// The maintainer signs with <c>minisign -S -m SHA256SUMS -t "realm-portal v1.2.0"</c>.
    /// </summary>
    public static string TrustedCommentFor(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        return "realm-portal " + tagName;
    }

    public static string TrustedCommentFor(ReleaseVersion version) => TrustedCommentFor(version.TagName);
}
