using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>
/// Persists update-check and status-probe settings in <c>network-settings.json</c>
/// (docs/updates-and-status.md). Same lock, bounded read, and <c>0600</c> write pattern as storage-settings.
/// </summary>
internal sealed class NetworkSettingsStore(
    string configDirectory,
    IAtomicFileWriter writer,
    ConfigDirectoryLock? directoryLock = null)
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path = Path.Combine(configDirectory, "network-settings.json");
    private readonly ConfigDirectoryLock _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);

    public NetworkSettings Get()
    {
        var file = Load();
        return file is null ? NetworkSettings.Default : ToModel(file);
    }

    public void Set(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using (_directoryLock.Acquire())
        {
            var existing = LoadUnlocked() ?? new NetworkFile { Version = FormatVersion };
            var file = Apply(existing, settings);
            file.Version = FormatVersion;
            Write(file);
        }
    }

    private NetworkFile? Load()
    {
        if (!File.Exists(_path))
            return null;

        return PortalConfigFile.WithConfigLock(LoadUnlocked);
    }

    private NetworkFile? LoadUnlocked()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var file = JsonSerializer.Deserialize<NetworkFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                       ?? throw new StoreException($"'{_path}' is empty.");
            if (file.Version != FormatVersion)
                throw new StoreException($"'{_path}' has unsupported format version {file.Version}.");
            return file;
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{_path}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{_path}' cannot be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"'{_path}' cannot be read: {ex.Message}", ex);
        }
    }

    private void Write(NetworkFile file)
    {
        PortalConfigFile.WithConfigLock(() =>
        {
            var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
            writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
            PortalConfigFile.AfterWrite(_path);
        });
    }

    private static NetworkSettings ToModel(NetworkFile file) =>
        new(
            UpdatesAutoCheck: file.Updates?.AutoCheck ?? true,
            UpdatesLastAttemptUtc: ParseUtc(file.Updates?.LastAttemptUtc),
            UpdatesEtag: NormalizeOptional(file.Updates?.Etag),
            UpdatesSkippedVersion: NormalizeOptional(file.Updates?.SkippedVersion),
            UpdatesLaterVersion: NormalizeOptional(file.Updates?.LaterVersion),
            UpdatesLaterUntilUtc: ParseUtc(file.Updates?.LaterUntilUtc),
            StatusProbeEnabled: file.Status?.ProbeEnabled ?? true);

    private static NetworkFile Apply(NetworkFile existing, NetworkSettings settings)
    {
        existing.Updates ??= new UpdatesSection();
        existing.Status ??= new StatusSection();
        existing.Updates.AutoCheck = settings.UpdatesAutoCheck;
        existing.Updates.LastAttemptUtc = FormatUtc(settings.UpdatesLastAttemptUtc);
        existing.Updates.Etag = settings.UpdatesEtag;
        existing.Updates.SkippedVersion = settings.UpdatesSkippedVersion;
        existing.Updates.LaterVersion = settings.UpdatesLaterVersion;
        existing.Updates.LaterUntilUtc = FormatUtc(settings.UpdatesLaterUntilUtc);
        existing.Status.ProbeEnabled = settings.StatusProbeEnabled;
        return existing;
    }

    private static DateTimeOffset? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static string? FormatUtc(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class NetworkFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }

        [JsonPropertyName("updates")] public UpdatesSection? Updates { get; set; }

        [JsonPropertyName("status")] public StatusSection? Status { get; set; }
    }

    private sealed class UpdatesSection
    {
        [JsonPropertyName("auto_check")] public bool AutoCheck { get; set; } = true;

        [JsonPropertyName("last_attempt_utc")] public string? LastAttemptUtc { get; set; }

        [JsonPropertyName("etag")] public string? Etag { get; set; }

        [JsonPropertyName("skipped_version")] public string? SkippedVersion { get; set; }

        [JsonPropertyName("later_version")] public string? LaterVersion { get; set; }

        [JsonPropertyName("later_until_utc")] public string? LaterUntilUtc { get; set; }
    }

    private sealed class StatusSection
    {
        [JsonPropertyName("probe_enabled")] public bool ProbeEnabled { get; set; } = true;
    }
}
