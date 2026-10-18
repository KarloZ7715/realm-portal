namespace RealmPortal.Core.Clients;

/// <summary>Progress reported once per inspected source folder while <see cref="ClientScanner.Scan"/> runs.</summary>
/// <param name="CurrentSource">The root currently being inspected.</param>
/// <param name="DirectoriesInspected">How many candidate folders have been inspected so far.</param>
/// <param name="ClientsFound">How many clients have been found so far.</param>
public sealed record ClientScanProgress(string CurrentSource, int DirectoriesInspected, int ClientsFound);

/// <summary>Bounds and extra roots for <see cref="ClientScanner.Scan"/>.</summary>
public sealed record ClientScanOptions
{
    /// <summary>Standard platform locations only, with the default budgets.</summary>
    public static ClientScanOptions Default { get; } = new();

    /// <summary>When true (default) the standard Linux/Windows candidate locations are inspected.</summary>
    public bool IncludeDefaultRoots { get; init; } = true;

    /// <summary>Extra folders to inspect in addition to the standard locations (treated as <see cref="ClientOrigin.Discovered"/>).</summary>
    public IReadOnlyList<string> AdditionalRoots { get; init; } = [];

    /// <summary>Home folder used to build the standard locations; the process home when <c>null</c>.</summary>
    public string? HomeDirectory { get; init; }

    /// <summary>Ids already registered in <c>clients.json</c>; they are neither returned nor counted.</summary>
    public IReadOnlySet<string>? KnownClientIds { get; init; }

    /// <summary>Descent budget for a root without a source-specific one (the standard roots each have their own).</summary>
    public int MaxDepth { get; init; } = ClientScanner.DefaultMaxDepth;

    /// <summary>Hard cap on inspected folders across the whole scan (the specification's 2,000 directory bound).</summary>
    public int MaxDirectoriesVisited { get; init; } = ClientScanner.DefaultMaxDirectoriesVisited;
}
