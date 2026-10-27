using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Updates;

/// <summary>Parses and verifies <c>SHA256SUMS</c> lines (docs/updates-and-status.md).</summary>
public static partial class Sha256Sums
{
    public const int MaxBytes = 64 * 1024;

    [GeneratedRegex(@"^([0-9a-f]{64}) [ *]?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    public static byte[] FindDigest(string sumsText, string assetName)
    {
        ArgumentNullException.ThrowIfNull(sumsText);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        byte[]? found = null;
        var matches = 0;
        using var reader = new StringReader(sumsText.Replace("\r\n", "\n", StringComparison.Ordinal));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            var m = LineRegex().Match(line);
            if (!m.Success)
                continue;
            var name = m.Groups[2].Value;
            if (!string.Equals(name, assetName, StringComparison.Ordinal))
                continue;
            matches++;
            found = Convert.FromHexString(m.Groups[1].Value);
        }

        if (matches != 1 || found is null)
            throw new UpdateException(UpdateErrorCode.AssetMissing, $"SHA256SUMS has {matches} line(s) for '{assetName}'.");

        return found;
    }

    public static void EnsureMatches(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length
            || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new UpdateException(UpdateErrorCode.ChecksumMismatch, "SHA256 digest mismatch.");
        }
    }

    public static string FormatHex(byte[] digest) =>
        Convert.ToHexString(digest).ToLowerInvariant();
}
