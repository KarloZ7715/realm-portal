namespace RealmPortal.Core.Clients;

/// <summary>Client builds with a known patch label (the patches whose file layout <c>ClientFilesPlanner</c> supports).</summary>
public static class KnownClientBuilds
{
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [5875] = "1.12.1",
        [8606] = "2.4.3",
        [12340] = "3.3.5a",
        [15595] = "4.3.4",
        [18414] = "5.4.8",
    };

    /// <summary>Known build numbers and their patch labels, e.g. <c>12340 → "3.3.5a"</c>.</summary>
    public static IReadOnlyDictionary<int, string> All => Labels;

    /// <summary>Looks up the patch label of <paramref name="build"/>.</summary>
    public static bool TryGetPatchLabel(int build, out string label)
    {
        if (Labels.TryGetValue(build, out var found))
        {
            label = found;
            return true;
        }
        label = string.Empty;
        return false;
    }
}
