namespace RealmPortal.Core.Updates;

/// <summary>
/// HTTPS allowlist for update traffic (docs/updates-and-status.md): github.com, api.github.com,
/// or a host ending with <c>.githubusercontent.com</c> (leading-dot suffix), port 443 or default, no userinfo.
/// </summary>
public static class TrustedHttpsUrl
{
    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null)
            return false;
        if (!uri.IsAbsoluteUri)
            return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (uri.IsDefaultPort)
        {
            // default for https is 443
        }
        else if (uri.Port != 443)
        {
            return false;
        }

        var host = uri.IdnHost;
        if (string.IsNullOrEmpty(host))
            return false;
        host = host.ToLowerInvariant();
        if (host is "github.com" or "api.github.com")
            return true;
        return host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    public static void EnsureAllowed(Uri uri)
    {
        if (!IsAllowed(uri))
            throw new UpdateException(UpdateErrorCode.UntrustedRedirect, $"Untrusted URL host '{HostOnly(uri)}'.");
    }

    /// <summary>Host only for logs — never the full URL (redirect targets carry tokens).</summary>
    public static string HostOnly(Uri? uri) =>
        uri is null ? "(null)" : (string.IsNullOrEmpty(uri.IdnHost) ? "(empty)" : uri.IdnHost.ToLowerInvariant());
}
