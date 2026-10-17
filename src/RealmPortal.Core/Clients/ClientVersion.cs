namespace RealmPortal.Core.Clients;

/// <summary>The version of a game client, read from the version resource of its executable.</summary>
/// <param name="FileVersion">The four-part file version, e.g. <c>3.3.5.12340</c>.</param>
public sealed record ClientVersion(Version FileVersion)
{
    /// <summary>The client build number: the last component of <see cref="FileVersion"/> (e.g. <c>12340</c>).</summary>
    public int Build => FileVersion.Revision;

    /// <summary>Formats the version as <c>12340 (3.3.5.12340)</c>.</summary>
    public override string ToString() => $"{Build} ({FileVersion})";
}
