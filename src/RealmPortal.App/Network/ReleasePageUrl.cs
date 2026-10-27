using RealmPortal.Core.Updates;

namespace RealmPortal.App.Network;

/// <summary>
/// Hardened allowlist for "Open on GitHub" (docs/updates-and-status.md):
/// https only, <see cref="TrustedHttpsUrl"/> hosts, and this repo's <c>releases/tag/</c> path.
/// </summary>
public static class ReleasePageUrl
{
    public const string TagPrefix = "https://github.com/KarloZ7715/realm-portal/releases/tag/";

    public static bool TryGetTrusted(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (!TrustedHttpsUrl.IsAllowed(parsed))
            return false;
        if (!parsed.AbsoluteUri.StartsWith(TagPrefix, StringComparison.Ordinal))
            return false;
        uri = parsed;
        return true;
    }

    /// <summary>Uses a trusted HtmlUrl when present; otherwise builds the tag URL for <paramref name="offer"/>.</summary>
    public static string Resolve(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (offer.HtmlUrl is { Length: > 0 } html && TryGetTrusted(html, out _))
            return html;

        var tag = string.IsNullOrWhiteSpace(offer.TagName)
            ? "v" + offer.Version.TrimStart('v')
            : offer.TagName!;
        if (!tag.StartsWith('v'))
            tag = "v" + tag;
        return TagPrefix + tag;
    }
}
