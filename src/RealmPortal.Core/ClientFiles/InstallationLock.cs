namespace RealmPortal.Core.ClientFiles;

/// <summary>Exclusive per-installation lock held across apply, activate, and rollback (see <see cref="IClientFilesApplier.AcquireInstallationLock"/>).</summary>
public sealed class InstallationLock : IDisposable
{
    internal InstallationLock(FileStream stream, string installationId)
    {
        Stream = stream;
        InstallationId = installationId;
    }

    public string InstallationId { get; }

    internal FileStream Stream { get; }

    public void Dispose() => Stream.Dispose();
}
