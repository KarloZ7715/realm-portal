namespace RealmPortal.Core.Servers;

/// <summary>
/// The deduplication key of the discovery contract: <c>(NormalizedHost, NormalizedRealm, RequiredClientBuild)</c>,
/// compared case-insensitively. The registration layer uses <see cref="FromExtracted"/> for what the extractor
/// found and <see cref="FromServer"/> for an existing <c>servers.json</c> entry to decide whether a server is new.
/// </summary>
public sealed class RealmlistIdentity : IEquatable<RealmlistIdentity>
{
    private RealmlistIdentity(string normalizedHost, string normalizedRealm, int? requiredClientBuild)
    {
        NormalizedHost = normalizedHost;
        NormalizedRealm = normalizedRealm;
        RequiredClientBuild = requiredClientBuild;
    }

    /// <summary>The extracted host, trimmed and lowercased (port included when written).</summary>
    public string NormalizedHost { get; }

    /// <summary>The realm name, trimmed and lowercased; empty when the client names no realm.</summary>
    public string NormalizedRealm { get; }

    /// <summary>The client build the entry belongs to, or <c>null</c> when it is client-independent.</summary>
    public int? RequiredClientBuild { get; }

    /// <summary>Builds the key from raw values (an extracted host or a stored server's fields).</summary>
    public static RealmlistIdentity Create(string host, string? realm, int? requiredClientBuild) =>
        new(Normalize(host), Normalize(realm), requiredClientBuild);

    /// <summary>The key of an extracted server for the given client build.</summary>
    public static RealmlistIdentity FromExtracted(ExtractedServer server, int? requiredClientBuild) =>
        Create(server.Host, server.Realm, requiredClientBuild);

    /// <summary>The key of a stored server (its host, realm and required build).</summary>
    public static RealmlistIdentity FromServer(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return Create(server.Host, server.Realm, server.RequiredClientBuild);
    }

    public bool Equals(RealmlistIdentity? other) =>
        other is not null
        && RequiredClientBuild == other.RequiredClientBuild
        && string.Equals(NormalizedHost, other.NormalizedHost, StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizedRealm, other.NormalizedRealm, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as RealmlistIdentity);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedHost),
        StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedRealm),
        RequiredClientBuild);

    public override string ToString() =>
        $"{NormalizedHost} / {NormalizedRealm} / {RequiredClientBuild?.ToString() ?? "any"}";

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
