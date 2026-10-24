namespace RealmPortal.Core.Clients;

/// <summary>The version of a game client, read from the version resource of its executable.</summary>
/// <param name="FileVersion">The four-part file version, e.g. <c>3.3.5.12340</c>.</param>
/// <remarks>
/// Display rule (the only one; the CLI and the App must not compose their own):
/// <list type="bullet">
/// <item>Known build (see <see cref="KnownClientBuilds"/>): <c>"&lt;patch&gt; (&lt;build&gt;)"</c>, e.g. <c>3.3.5a (12340)</c>.</item>
/// <item>Unknown build: the first three file-version parts plus the build, e.g. <c>3.3.5 (12345)</c>.</item>
/// <item>No build (the last file-version part is 0 or missing): the patch label alone, e.g. <c>3.3.5</c>.</item>
/// </list>
/// </remarks>
public sealed record ClientVersion(Version FileVersion)
{
    /// <summary>The client build number: the last component of <see cref="FileVersion"/> (e.g. <c>12340</c>).</summary>
    public int Build => FileVersion.Revision;

    /// <summary><c>true</c> when <see cref="FileVersion"/> carries a build number (its last component is greater than 0).</summary>
    public bool HasBuild => Build > 0;

    /// <summary><c>true</c> when <see cref="Build"/> is in the <see cref="KnownClientBuilds"/> table.</summary>
    public bool IsKnownBuild => HasBuild && KnownClientBuilds.TryGetPatchLabel(Build, out _);

    /// <summary>
    /// The patch the client is, e.g. <c>3.3.5a</c> for build 12340. For a build not in <see cref="KnownClientBuilds"/>
    /// it falls back to the first three parts of <see cref="FileVersion"/> (e.g. <c>3.3.5</c>).
    /// </summary>
    public string PatchLabel =>
        HasBuild && KnownClientBuilds.TryGetPatchLabel(Build, out var label)
            ? label
            : $"{FileVersion.Major}.{FileVersion.Minor}.{Math.Max(FileVersion.Build, 0)}";

    /// <summary>The user-facing version: <c>3.3.5a (12340)</c>; <c>3.3.5 (12345)</c> for an unknown build; <see cref="PatchLabel"/> alone without a build.</summary>
    public string DisplayVersion => HasBuild ? $"{PatchLabel} ({Build})" : PatchLabel;

    /// <summary>Same as <see cref="DisplayVersion"/>.</summary>
    public override string ToString() => DisplayVersion;
}
