using System.Globalization;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Clients;

/// <summary>
/// A game client version such as <c>3.3.5</c> or <c>3.3.5.12340</c> (major.minor.patch with an optional build number).
/// </summary>
/// <remarks>
/// Textual form: <c>M.m.p</c> or <c>M.m.p.build</c>. When parsing, one trailing letter after the patch number
/// (for example <c>3.3.5a</c>) is accepted and ignored: the letter is only a marketing suffix and the build number
/// is what really tells client revisions apart.
/// </remarks>
public readonly partial record struct ClientVersion(int Major, int Minor, int Patch, int? Build = null)
{
    [GeneratedRegex(@"^(\d{1,4})\.(\d{1,4})\.(\d{1,4})[a-zA-Z]?(?:\.(\d{1,9}))?$")]
    private static partial Regex Pattern();

    /// <summary>Parses <c>M.m.p</c>, <c>M.m.pL</c>, <c>M.m.p.build</c> or <c>M.m.pL.build</c>.</summary>
    public static bool TryParse(string? text, out ClientVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Pattern().Match(text.Trim());
        if (!match.Success)
            return false;

        var culture = CultureInfo.InvariantCulture;
        int? build = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, culture) : null;
        version = new ClientVersion(
            int.Parse(match.Groups[1].Value, culture),
            int.Parse(match.Groups[2].Value, culture),
            int.Parse(match.Groups[3].Value, culture),
            build);
        return true;
    }

    /// <summary>Parses a version or throws <see cref="FormatException"/>.</summary>
    public static ClientVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a valid client version (expected e.g. 3.3.5 or 3.3.5.12340).");

    /// <summary>
    /// True when a client of this version can play on a server that requires <paramref name="required"/>:
    /// major, minor and patch must be equal, and the builds must be equal when both sides specify one.
    /// </summary>
    public bool Satisfies(ClientVersion required) =>
        Major == required.Major
        && Minor == required.Minor
        && Patch == required.Patch
        && (Build is null || required.Build is null || Build == required.Build);

    public override string ToString()
    {
        var core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return Build is { } build ? string.Create(CultureInfo.InvariantCulture, $"{core}.{build}") : core;
    }
}
