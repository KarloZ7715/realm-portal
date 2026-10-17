namespace RealmPortal.Core.ClientFiles;

/// <summary>Whether a client path was a symbolic link before apply.</summary>
internal readonly record struct SymlinkSnapshot(bool IsSymlink, string? Target)
{
    public static SymlinkSnapshot? Capture(string path)
    {
        if (!File.Exists(path))
            return null;
        var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
        return target is null ? null : new SymlinkSnapshot(true, target.FullName);
    }

    public void Restore(string path)
    {
        if (!IsSymlink || string.IsNullOrEmpty(Target))
            return;
        if (File.Exists(path))
            File.Delete(path);
        File.CreateSymbolicLink(path, Target);
    }
}
