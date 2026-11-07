using System.Text;

namespace RealmPortal.Core.Updates.Signing;

/// <summary>
/// The one place where a release's <c>SHA256SUMS</c> becomes trusted (#116). Anything that reads digests from
/// <c>SHA256SUMS</c> (the in-app download today, automatic downloads later) should go through here first.
/// </summary>
public static class SignedSha256Sums
{
    /// <summary>Largest <c>SHA256SUMS.minisig</c> the updater will download. A real one is about 300 bytes.</summary>
    public const int MaxSignatureBytes = 16 * 1024;

    /// <summary>
    /// Checks <paramref name="sums"/> against <paramref name="signature"/> for the release tagged <paramref name="tagName"/>.
    /// The expected trusted comment is built from the raw tag (pre-release suffix included), not from a parsed version.
    /// </summary>
    /// <returns><see cref="SignatureStatus.Valid"/> only when the digests can be trusted.</returns>
    public static SignatureStatus Check(
        IReleaseSignatureVerifier verifier,
        string tagName,
        ReadOnlySpan<byte> sums,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        var expected = ReleaseSignature.TrustedCommentFor(tagName);
        if (sums.Length > Sha256Sums.MaxBytes || signature.Length > MaxSignatureBytes)
            return SignatureStatus.TooLarge;
        return verifier.Verify(sums, signature, expected);
    }

    /// <summary>
    /// Same as <see cref="Check"/>, but returns the verified text and throws <see cref="UpdateException"/> with
    /// <see cref="UpdateErrorCode.SignatureInvalid"/> for any status other than <see cref="SignatureStatus.Valid"/>.
    /// </summary>
    public static string EnsureValid(
        IReleaseSignatureVerifier verifier,
        string tagName,
        ReadOnlySpan<byte> sums,
        ReadOnlySpan<byte> signature)
    {
        var status = Check(verifier, tagName, sums, signature);
        if (status != SignatureStatus.Valid)
        {
            throw new UpdateException(
                UpdateErrorCode.SignatureInvalid,
                $"SHA256SUMS signature check failed for {Printable(tagName)}: {status}.")
            {
                Signature = status,
            };
        }

        return Encoding.UTF8.GetString(sums);
    }

    /// <summary>
    /// Makes a trusted comment or tag safe to log or show: control characters (including newlines and bidi controls
    /// that could fake a different line) become <c>\uXXXX</c>, and the result is cut at 200 characters.
    /// </summary>
    public static string Printable(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(Math.Min(text.Length, 200));
        foreach (var c in text)
        {
            if (sb.Length >= 200)
            {
                sb.Append('…');
                break;
            }

            if (char.IsControl(c) || c is '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'))
                sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }
}
