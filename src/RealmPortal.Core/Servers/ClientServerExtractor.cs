using System.Text.RegularExpressions;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Servers;

/// <summary>
/// Reads the server a client is already configured for out of the client's own files:
/// <c>Data/&lt;locale&gt;/realmlist.wtf</c>, a root <c>realmlist.wtf</c> and <c>WTF/Config.wtf</c>.
/// Everything is read through bounded, non-following reads that stay inside the client folder, and nothing is
/// written, so pointing this at a client is safe at any time. The extractor never registers or activates a server.
/// </summary>
public sealed class ClientServerExtractor
{
    /// <summary>Cap for <c>realmlist.wtf</c> (the realmlist contract's own limit).</summary>
    public const int MaxRealmlistBytes = RealmlistAddress.MaxRealmlistBytes;

    /// <summary>Cap for <c>WTF/Config.wtf</c>.</summary>
    public const int MaxConfigBytes = 64 * 1024;

    private static readonly Regex LocaleFolder = new("^[a-z]{2}[A-Z]{2}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex ConfigLine =
        new(@"^\s*SET\s+(?<key>[A-Za-z]+)\s+(?<value>.*?)\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the configured server of <paramref name="clientFolder"/>, or <c>null</c> when the folder has no
    /// usable configuration (missing, unreadable, invalid or a stock official placeholder host).
    /// </summary>
    /// <param name="clientFolder">The client folder (the one with <c>Wow.exe</c>).</param>
    /// <param name="clientBuild">The client build, used to pick the file layout; its build is not applied to the result.</param>
    public ExtractedServer? Extract(string clientFolder, int? clientBuild = null)
    {
        if (string.IsNullOrWhiteSpace(clientFolder))
            return null;

        string root;
        try
        {
            root = ClientPaths.CanonicalRoot(clientFolder);
        }
        catch (Exception ex) when (ex is ClientFilesException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var configPath = ResolveManagedPath(root, "WTF/Config.wtf");
        var config = configPath is null ? null : ReadConfig(configPath);

        string? host = null;
        string? patchlist = null;
        string? realmlistPath = null;
        foreach (var candidate in RealmlistCandidates(root, clientBuild))
        {
            var blob = ReadRealmlist(candidate);
            if (blob is null)
                continue;

            if (host is null && IsUsableHost(blob.Host))
            {
                host = blob.Host;
                realmlistPath = candidate;
                if (blob.Values.TryGetValue(RealmlistBlob.PatchlistKey, out var value) && IsUsablePatchlist(value))
                    patchlist = value;
            }

            if (host is not null)
                break;
        }

        if (host is null && config is not null && TryGetConfigHost(config, out var configHost))
            host = configHost;
        if (patchlist is null && config is not null
            && config.TryGetValue("patchlist", out var configPatchlist) && IsUsablePatchlist(configPatchlist))
        {
            patchlist = configPatchlist;
        }

        if (host is null)
            return null;

        var realm = ValueOrNull(config, "realmName");
        var account = ValueOrNull(config, "accountName");

        // Reuse the server contract itself: what comes back is exactly what Server.Create (and therefore the store)
        // accepts, so the registration layer can pass these values through unchanged.
        try
        {
            _ = Server.Create(
                "server",
                host,
                realm,
                patchlist is null ? null : [$"set patchlist {patchlist}"],
                clientBuild is > 0 ? clientBuild : null,
                account);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidRealmlistException)
        {
            return null;
        }

        return new ExtractedServer(host, realm, patchlist, account, realmlistPath, config?.Count > 0 ? configPath : null);
    }

    /// <summary>The realmlist files to try, in the layout's preferred order for the client build.</summary>
    private static IEnumerable<string> RealmlistCandidates(string root, int? clientBuild)
    {
        var rootRealmlist = ResolveManagedPath(root, "realmlist.wtf");
        var localeRealmlists = new List<string>();
        foreach (var locale in LocaleFolders(root))
        {
            if (ResolveManagedPath(root, $"Data/{locale}/realmlist.wtf") is { } path)
                localeRealmlists.Add(path);
        }

        // 1.x and 2.x clients keep the file in the root; 3.x and 4.x clients keep it per locale folder.
        var localesFirst = clientBuild is not (5875 or 8606);
        if (localesFirst)
        {
            foreach (var path in localeRealmlists)
                yield return path;
        }

        if (rootRealmlist is not null)
            yield return rootRealmlist;

        if (!localesFirst)
        {
            foreach (var path in localeRealmlists)
                yield return path;
        }
    }

    /// <summary>The existing locale folders (<c>Data/enUS</c>, ...) of the client.</summary>
    private static IReadOnlyList<string> LocaleFolders(string root)
    {
        try
        {
            if (CaseInsensitivePath.FindDirectory(root, "Data") is not { } data)
                return [];

            var dataPath = Path.Combine(root, data);
            return CaseInsensitivePath.ChildDirectories(dataPath)
                .Where(locale => LocaleFolder.IsMatch(locale))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static RealmlistBlob? ReadRealmlist(string path)
    {
        if (!SafeFileRead.TryReadTextNonFollowing(path, MaxRealmlistBytes, out var text, out _))
            return null;

        try
        {
            var blob = RealmlistBlob.Parse(text, allowPortal: false);
            RealmlistAddress.ValidateRealmlistHost(blob.Host, 1);
            return blob;
        }
        catch (Exception ex) when (ex is ClientFilesException or InvalidRealmlistException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ReadConfig(string path)
    {
        if (!SafeFileRead.TryReadTextNonFollowing(path, MaxConfigBytes, out var text, out _))
            return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var match = ConfigLine.Match(raw.TrimEnd('\r'));
            if (!match.Success)
                continue;

            values.TryAdd(match.Groups["key"].Value, Unquote(match.Groups["value"].Value));
        }

        return values;
    }

    /// <summary>A host from <c>SET realmList</c>, or from a <c>SET portal</c> that is an address rather than a region code.</summary>
    private static bool TryGetConfigHost(IReadOnlyDictionary<string, string> config, out string host)
    {
        host = string.Empty;
        if (ValueOrNull(config, "realmList") is { } realmList && IsUsableHost(realmList))
        {
            host = realmList;
            return true;
        }

        if (ValueOrNull(config, "portal") is { } portal && LooksLikeAddress(portal) && IsUsableHost(portal))
        {
            host = portal;
            return true;
        }

        return false;
    }

    private static bool IsUsableHost(string host) =>
        host.Length > 0 && !IsOfficialPlaceholder(host) && Validates(() => RealmlistAddress.ValidateRealmlistHost(host, 1));

    private static bool IsUsablePatchlist(string patchlist) =>
        patchlist.Length > 0 && Validates(() => RealmlistAddress.ValidatePatchlistValue(patchlist, 1));

    private static bool Validates(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (InvalidRealmlistException)
        {
            return false;
        }
    }

    /// <summary>Stock official hosts are never imported; the client is pointing at Blizzard, not at a private server.</summary>
    private static bool IsOfficialPlaceholder(string host)
    {
        var value = host.Trim().TrimEnd('.').ToLowerInvariant();
        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
            value = value[..colon];

        return value is "battle.net" or "worldofwarcraft.com" or "blizzard.com"
               || value.EndsWith(".battle.net", StringComparison.Ordinal)
               || value.EndsWith(".worldofwarcraft.com", StringComparison.Ordinal)
               || value.EndsWith(".blizzard.com", StringComparison.Ordinal);
    }

    /// <summary>A bare region code such as <c>us</c> in <c>SET portal "us"</c> is not a host.</summary>
    private static bool LooksLikeAddress(string value) =>
        value.Contains('.') || value.Contains(':') || value.Contains('[');

    private static string? ValueOrNull(IReadOnlyDictionary<string, string>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var value))
            return null;
        value = value.Trim();
        return value.Length > 0 ? value : null;
    }

    private static string? ResolveManagedPath(string root, string relativePath)
    {
        try
        {
            return ClientPaths.ResolveManagedPath(root, relativePath);
        }
        catch (Exception ex) when (ex is ClientFilesException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Strips one layer of quotes and the escapes <c>Config.wtf</c> uses for quotes and backslashes.</summary>
    private static string Unquote(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }
}
