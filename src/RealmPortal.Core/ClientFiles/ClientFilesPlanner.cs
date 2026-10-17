using System.Text.RegularExpressions;
using RealmPortal.Core.Clients;

namespace RealmPortal.Core.ClientFiles;

/// <summary>
/// Dry-run planner: decides which client files to change for a server's realmlist and computes their exact new
/// bytes. It only reads the client directory.
/// </summary>
/// <remarks>
/// Layout by client major version (Linux is case-sensitive, so every path segment is resolved case-insensitively
/// and the real name on disk is kept):
/// <list type="bullet">
/// <item>1.x and 2.x (5875, 8606): <c>realmlist.wtf</c> in the root; <c>WTF/Config.wtf</c> only if it already exists
/// and only its <c>realmName</c> line.</item>
/// <item>3.x and 4.x (12340, 15595): <c>Data/&lt;locale&gt;/realmlist.wtf</c> for every existing locale folder
/// (<c>^[a-z]{2}[A-Z]{2}$</c>) plus <c>WTF/Config.wtf</c> (<c>realmList</c>, <c>patchlist</c> if in the text, <c>realmName</c>).</item>
/// <item>5.x (18414): <c>WTF/Config.wtf</c> (<c>realmList</c>, <c>patchlist</c> if in the text, <c>realmName</c>).</item>
/// <item>6.x and 7.x (WoD, Legion): <c>WTF/Config.wtf</c> (<c>portal</c>, <c>realmName</c>); <c>portal</c> comes from the
/// text's <c>set portal</c> line when present, otherwise from the <c>set realmlist</c> host.</item>
/// </list>
/// The server's realmlist text is the source of truth and is written as is; nothing is invented. An extra
/// <c>realmlist.wtf</c> that already exists (root or any existing locale folder) is updated too, but new files are
/// only created at the layout's main paths. Locale folders are never created and no other file is touched
/// (<c>accountName</c>, <c>locale</c> and caches are never written). <c>WTF/</c> is created when <c>Config.wtf</c>
/// is a main path and missing; a new <c>Config.wtf</c> holds only managed keys.
/// </remarks>
public sealed class ClientFilesPlanner : IClientFilesPlanner
{
    private const string RealmlistFile = "realmlist.wtf";
    private static readonly Regex LocaleFolder = new("^[a-z]{2}[A-Z]{2}\\z", RegexOptions.CultureInvariant);

    private enum RealmlistMain { None, Root, Locales }

    private enum ConfigMode { None, RealmNameOnly, Realmlist, Portal }

    private sealed record Layout(RealmlistMain Realmlist, ConfigMode Config, bool AllowPortal);

    public ClientFilesPlan Plan(ClientFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.ValidateIds();
        var layout = LayoutFor(request.Version);
        var blob = RealmlistBlob.Parse(request.Realmlist, layout.AllowPortal);
        RealmlistBlob.ValidateRealm(request.Realm);

        var root = request.ClientRoot;
        if (!ClientRootExists(root))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, root ?? string.Empty,
                $"The client directory '{root}' does not exist.");

        var edits = new List<ClientFileEdit>();
        try
        {
            foreach (var (segments, isMain) in RealmlistCandidates(root, layout))
                AddRealmlistEdit(edits, root, segments, isMain, blob);

            AddConfigEdit(edits, root, layout, blob, request.Realm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, root, "read");
        }

        return new ClientFilesPlan(request.InstallationId, request.ProfileId, root, request.Version,
            request.Realmlist, request.Realm, edits);
    }

    private static Layout LayoutFor(Clients.ClientVersion version) => version.FileVersion.Major switch
    {
        1 or 2 => new Layout(RealmlistMain.Root, ConfigMode.RealmNameOnly, false),
        3 or 4 => new Layout(RealmlistMain.Locales, ConfigMode.Realmlist, false),
        5 => new Layout(RealmlistMain.None, ConfigMode.Realmlist, false),
        6 or 7 => new Layout(RealmlistMain.None, ConfigMode.Portal, true),
        _ => throw new ClientFilesException(ClientFilesErrorCode.UnsupportedClient, string.Empty,
            $"Client version {version.FileVersion} has no known realm configuration layout."),
    };

    private static bool ClientRootExists(string? root)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Root realmlist.wtf, then every existing locale folder, in a deterministic order.</summary>
    private static IEnumerable<(string[] Segments, bool IsMain)> RealmlistCandidates(string root, Layout layout)
    {
        yield return ([RealmlistFile], layout.Realmlist == RealmlistMain.Root);

        var data = CaseInsensitivePath.FindDirectory(root, "Data");
        if (data is null)
            yield break;

        foreach (var locale in CaseInsensitivePath.ChildDirectories(Path.Combine(root, data)))
        {
            if (LocaleFolder.IsMatch(locale))
                yield return ([data, locale, RealmlistFile], layout.Realmlist == RealmlistMain.Locales);
        }
    }

    private static void AddRealmlistEdit(List<ClientFileEdit> edits, string root, string[] candidate, bool isMain, RealmlistBlob blob)
    {
        var (segments, exists) = CaseInsensitivePath.Resolve(root, candidate);
        if (!exists && !isMain)
            return; // Extra candidates are only updated when they already exist.

        AddEdit(edits, root, segments, exists, existing => ClientFileContent.BuildRealmlist(existing, blob));
    }

    private static void AddConfigEdit(List<ClientFileEdit> edits, string root, Layout layout, RealmlistBlob blob, string realm)
    {
        if (layout.Config == ConfigMode.None)
            return;

        var (segments, exists) = CaseInsensitivePath.Resolve(root, "WTF", "Config.wtf");
        if (!exists && layout.Config == ConfigMode.RealmNameOnly)
            return; // Old clients: never create Config.wtf.

        var managed = new List<KeyValuePair<string, string?>>();
        switch (layout.Config)
        {
            case ConfigMode.Realmlist:
                managed.Add(new("realmList", blob.Host));
                if (blob.Values.TryGetValue(RealmlistBlob.PatchlistKey, out var patchlist))
                    managed.Add(new("patchlist", patchlist));
                break;
            case ConfigMode.Portal:
                managed.Add(new("portal", blob.Values.GetValueOrDefault(RealmlistBlob.PortalKey, blob.Host)));
                break;
        }

        managed.Add(new("realmName", string.IsNullOrEmpty(realm) ? null : realm));
        AddEdit(edits, root, segments, exists, existing => ClientFileContent.BuildConfig(existing, managed));
    }

    private static void AddEdit(List<ClientFileEdit> edits, string root, string[] segments, bool exists, Func<byte[]?, byte[]> build)
    {
        var relative = string.Join('/', segments);
        if (edits.Any(e => string.Equals(e.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
            return;

        var fullPath = Path.Combine([root, .. segments]);
        var existing = exists ? File.ReadAllBytes(fullPath) : null;
        var updated = build(existing);
        var after = ClientFilesIo.Sha256Hex(updated);
        var before = existing is null ? null : ClientFilesIo.Sha256Hex(existing);
        if (before == after)
            return; // Already up to date.

        edits.Add(new ClientFileEdit(relative, exists ? ClientEditKind.Replace : ClientEditKind.Create, updated, before, after));
    }
}
