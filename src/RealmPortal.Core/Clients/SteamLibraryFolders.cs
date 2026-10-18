using System.Text.RegularExpressions;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Clients;

/// <summary>
/// Reads the library folders of a Steam installation from <c>steamapps/libraryfolders.vdf</c> (and the older
/// <c>config/libraryfolders.vdf</c>). The file is read in a bounded, non-following read and only
/// <c>"path"</c> values are taken, so a hostile file cannot make the scanner read or run anything else.
/// </summary>
internal static partial class SteamLibraryFolders
{
    internal const int MaxBytes = 256 * 1024;

    /// <summary>The library roots recorded by the given Steam installation; empty when no readable file is found.</summary>
    internal static IReadOnlyList<string> ReadLibraries(string steamRoot)
    {
        foreach (var vdf in VdfCandidates(steamRoot))
        {
            if (!SafeFileRead.TryReadTextNonFollowing(vdf, MaxBytes, out var text, out _))
                continue;

            var libraries = new List<string>();
            foreach (Match match in Pair().Matches(text))
            {
                var key = match.Groups["key"].Value;
                // Newer files use "path", older ones number the libraries ("0", "1", ...).
                if (key != "path" && !key.All(char.IsAsciiDigit))
                    continue;

                var value = Unescape(match.Groups["value"].Value);
                if (IsAbsolutePath(value))
                    libraries.Add(value);
            }

            if (libraries.Count > 0)
                return libraries.Distinct(StringComparer.Ordinal).ToList();
        }

        return [];
    }

    private static IEnumerable<string> VdfCandidates(string steamRoot)
    {
        yield return Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        yield return Path.Combine(steamRoot, "config", "libraryfolders.vdf");
    }

    private static bool IsAbsolutePath(string value) =>
        value.StartsWith('/') || Path.IsPathRooted(value);

    /// <summary>VDF escapes backslashes and quotes with a backslash.</summary>
    private static string Unescape(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);

    [GeneratedRegex(@"""(?<key>[^""]+)""\s+""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();
}
