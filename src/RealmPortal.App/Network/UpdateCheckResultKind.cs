namespace RealmPortal.App.Network;

/// <summary>Outcome of <see cref="IUpdateChecker.CheckAsync"/>.</summary>
public enum UpdateCheckResultKind
{
    None = 0,
    UpToDate = 1,
    Offer = 2,
    RateLimited = 3,
    Failed = 4,
}
