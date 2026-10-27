using System.Text.Json.Serialization;

namespace RealmPortal.App.Network;

/// <summary>
/// Values from <c>network-settings.json</c> (format version 1). Property names match
/// <c>docs/updates-and-status.md</c> exactly so Backend's store can replace this App adapter.
/// </summary>
public sealed class NetworkSettings
{
    public const int FormatVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = FormatVersion;

    [JsonPropertyName("updates.auto_check")]
    public bool UpdatesAutoCheck { get; set; } = true;

    [JsonPropertyName("updates.last_attempt_utc")]
    public DateTimeOffset? UpdatesLastAttemptUtc { get; set; }

    [JsonPropertyName("updates.etag")]
    public string? UpdatesEtag { get; set; }

    [JsonPropertyName("updates.skipped_version")]
    public string? UpdatesSkippedVersion { get; set; }

    [JsonPropertyName("updates.later_version")]
    public string? UpdatesLaterVersion { get; set; }

    [JsonPropertyName("updates.later_until_utc")]
    public DateTimeOffset? UpdatesLaterUntilUtc { get; set; }

    [JsonPropertyName("status.probe_enabled")]
    public bool StatusProbeEnabled { get; set; } = true;
}
