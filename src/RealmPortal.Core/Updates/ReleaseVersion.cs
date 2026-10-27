using System.Globalization;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Updates;

/// <summary>Strict SemVer x.y.z parser for release tags and the running InformationalVersion.</summary>
public readonly partial struct ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private static readonly Regex TagPattern = TagRegex();

    public ReleaseVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public string WithoutPrefix => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    public string TagName => "v" + WithoutPrefix;

    public static bool TryParseTag(string? tagName, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(tagName))
            return false;
        var m = TagPattern.Match(tagName.Trim());
        if (!m.Success)
            return false;
        if (!TryComponent(m.Groups[1].Value, out var major)
            || !TryComponent(m.Groups[2].Value, out var minor)
            || !TryComponent(m.Groups[3].Value, out var patch))
            return false;
        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    /// <summary>Parses InformationalVersion with optional leading <c>v</c> and without <c>+metadata</c> / pre-release.</summary>
    public static bool TryParseInformational(string? informational, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(informational))
            return false;
        var text = informational.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];
        // Reject pre-release suffix (1.0.0-rc.1)
        if (text.Contains('-', StringComparison.Ordinal))
            return false;
        if (!text.StartsWith('v') && !text.StartsWith('V'))
            text = "v" + text;
        return TryParseTag(text, out version);
    }

    private static bool TryComponent(string text, out int value)
    {
        value = 0;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            return false;
        // Overflow of SemVer components is rejected by int.TryParse already for values > int.MaxValue;
        // reject leading zeros except plain "0" via the regex.
        return value >= 0;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public override string ToString() => WithoutPrefix;

    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;
    public static bool operator ==(ReleaseVersion a, ReleaseVersion b) => a.Equals(b);
    public static bool operator !=(ReleaseVersion a, ReleaseVersion b) => !a.Equals(b);

    [GeneratedRegex(@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();
}
