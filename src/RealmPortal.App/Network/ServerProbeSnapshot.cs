namespace RealmPortal.App.Network;

/// <summary>Latest probe result for one server, published by <see cref="IProbeScheduler"/>.</summary>
public sealed record ServerProbeSnapshot(
    string ServerId,
    LoginReachability Status,
    string? Reason,
    DateTimeOffset? CheckedAtUtc);
