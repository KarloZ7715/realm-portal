namespace RealmPortal.Core.Servers;

/// <summary>
/// A server description read out of a client's own configuration by <see cref="ClientServerExtractor"/>.
/// Nothing has been registered yet: the caller decides whether a server with the same
/// <see cref="RealmlistIdentity"/> already exists and registers this one only when it does not.
/// </summary>
/// <param name="Host">The realmlist host (validated); never a stock official placeholder.</param>
/// <param name="Realm">The realm name from <c>SET realmName</c>, or <c>null</c>.</param>
/// <param name="PatchlistHost">The <c>set patchlist</c> / <c>SET patchlist</c> value, or <c>null</c>.</param>
/// <param name="Account">The remembered account name from <c>SET accountName</c> (never a password), or <c>null</c>.</param>
/// <param name="RealmlistPath">The <c>realmlist.wtf</c> the host came from, or <c>null</c> when it came from <c>Config.wtf</c>.</param>
/// <param name="ConfigPath">The <c>WTF/Config.wtf</c> that was read, or <c>null</c> when it does not exist.</param>
public sealed record ExtractedServer(
    string Host,
    string? Realm,
    string? PatchlistHost,
    string? Account,
    string? RealmlistPath,
    string? ConfigPath)
{
    /// <summary>Extra realmlist lines for <c>Server.Create</c>: the <c>set patchlist</c> line when there is one.</summary>
    public IReadOnlyList<string>? ExtraRealmlistLines =>
        PatchlistHost is null ? null : [$"set patchlist {PatchlistHost}"];

    /// <summary>The deduplication key for the client build this extraction belongs to.</summary>
    public RealmlistIdentity Identity(int? requiredClientBuild) => RealmlistIdentity.FromExtracted(this, requiredClientBuild);
}
