namespace RealmPortal.App.Network;

/// <summary>No network: automatic checks are silent no-ops; manual checks report failure.</summary>
public sealed class NullUpdateChecker : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == UpdateCheckKind.Automatic)
            return Task.FromResult(UpdateCheckResult.None);
        return Task.FromResult(UpdateCheckResult.Failed);
    }

    public void RemindLater(UpdateOffer offer) { }

    public void SkipThisVersion(UpdateOffer offer) { }

    public Task<UpdateInstallOutcome?> InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<UpdateInstallOutcome?>(null);
    }
}
