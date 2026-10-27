namespace RealmPortal.App.Network;

/// <summary>Test double: returns a configured result and records RemindLater / Skip / Install calls.</summary>
public sealed class RecordingUpdateChecker : IUpdateChecker
{
    private readonly Func<UpdateCheckKind, UpdateCheckResult> _onCheck;

    public RecordingUpdateChecker(Func<UpdateCheckKind, UpdateCheckResult>? onCheck = null)
    {
        _onCheck = onCheck ?? (_ => UpdateCheckResult.None);
    }

    public List<(UpdateCheckKind Kind, DateTimeOffset At)> Checks { get; } = [];
    public List<UpdateOffer> RemindLaterCalls { get; } = [];
    public List<UpdateOffer> SkipCalls { get; } = [];
    public List<UpdateOffer> InstallCalls { get; } = [];

    public Task<UpdateCheckResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default)
    {
        Checks.Add((kind, DateTimeOffset.UtcNow));
        return Task.FromResult(_onCheck(kind));
    }

    public void RemindLater(UpdateOffer offer) => RemindLaterCalls.Add(offer);

    public void SkipThisVersion(UpdateOffer offer) => SkipCalls.Add(offer);

    public Task<UpdateInstallOutcome?> InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
    {
        InstallCalls.Add(offer);
        return Task.FromResult<UpdateInstallOutcome?>(null);
    }
}
