namespace RealmPortal.App.Network;

/// <summary>Discriminated result of an update check.</summary>
public sealed record UpdateCheckResult(UpdateCheckResultKind Kind, UpdateOffer? Offer = null)
{
    public static UpdateCheckResult None { get; } = new(UpdateCheckResultKind.None);
    public static UpdateCheckResult UpToDate { get; } = new(UpdateCheckResultKind.UpToDate);
    public static UpdateCheckResult RateLimited { get; } = new(UpdateCheckResultKind.RateLimited);
    public static UpdateCheckResult Failed { get; } = new(UpdateCheckResultKind.Failed);

    public static UpdateCheckResult FromOffer(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new UpdateCheckResult(UpdateCheckResultKind.Offer, offer);
    }
}
