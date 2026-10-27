namespace RealmPortal.App.Network;

/// <summary>
/// Stub until Core's GitHub Releases checker lands. Automatic → None (silent);
/// Manual → Failed so Settings can show a soft status without inventing Core APIs.
/// </summary>
public sealed class NullUpdateChecker : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default)
    {
        // Automatic: silent — no dialog, no toast.
        if (kind == UpdateCheckKind.Automatic)
            return Task.FromResult(UpdateCheckResult.None);

        // Manual: soft failure until Backend wires the real checker.
        return Task.FromResult(UpdateCheckResult.Failed);
    }

    public void RemindLater(UpdateOffer offer) { }

    public void SkipThisVersion(UpdateOffer offer) { }

    public Task InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
