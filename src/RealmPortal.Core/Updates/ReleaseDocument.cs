using System.Text.Json;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.Updates;

/// <summary>Parsed GitHub release JSON fields needed for the update notice.</summary>
public sealed record ReleaseDocument(
    ReleaseVersion Version,
    string TagName,
    string ReleaseNotesPlain,
    string? HtmlUrl,
    IReadOnlyList<ReleaseAsset> Assets);

public sealed record ReleaseAsset(string Name, string BrowserDownloadUrl, long Size);

public static class ReleaseDocumentParser
{
    public const int MaxNotesChars = 20_000;

    public static ReleaseDocument? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (GetBool(root, "draft") || GetBool(root, "prerelease"))
                return null;

            var tag = GetString(root, "tag_name");
            if (!ReleaseVersion.TryParseTag(tag, out var version))
                return null;

            var body = GetString(root, "body") ?? "";
            if (body.Length > MaxNotesChars)
                body = body[..MaxNotesChars];
            // Strip tags lightly — UI shows plain text only; keep as-is but remove NULs.
            body = body.Replace("\0", "", StringComparison.Ordinal);

            var htmlUrl = GetString(root, "html_url");
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var name = GetString(item, "name");
                    var url = GetString(item, "browser_download_url");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                        continue;
                    long size = 0;
                    if (item.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var s))
                        size = s;
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }

            return new ReleaseDocument(version, tag!, body, htmlUrl, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ResolveHtmlUrl(ReleaseDocument release)
    {
        const string prefix = "https://github.com/KarloZ7715/realm-portal/releases/tag/";
        if (release.HtmlUrl is { Length: > 0 } url
            && url.StartsWith(prefix, StringComparison.Ordinal)
            && TrustedHttpsUrl.IsAllowed(new Uri(url)))
        {
            return url;
        }

        return prefix + release.Version.TagName;
    }

    public static string AssetNameFor(InstallKind kind, ReleaseVersion version) =>
        kind switch
        {
            InstallKind.AppImage => $"RealmPortal-{version.WithoutPrefix}-x86_64.AppImage",
            InstallKind.Deb => $"realm-portal_{version.WithoutPrefix}_amd64.deb",
            InstallKind.WindowsZip => $"RealmPortal-{version.WithoutPrefix}-win-x64.zip",
            _ => "",
        };

    public static string ExpectedDownloadUrl(ReleaseVersion version, string assetName) =>
        $"https://github.com/KarloZ7715/realm-portal/releases/download/{version.TagName}/{assetName}";

    private static bool GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
