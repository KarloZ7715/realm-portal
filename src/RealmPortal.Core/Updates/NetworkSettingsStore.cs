using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;

namespace RealmPortal.Core.Updates;

/// <summary>
/// Reads and writes <c>network-settings.json</c> in the config directory
/// (<c>version: 1</c>, <c>0600</c>, <see cref="ConfigDirectoryLock"/> — same pattern as storage-settings).
/// </summary>
public sealed class NetworkSettingsStore
{
    public const string FileName = "network-settings.json";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private readonly IAtomicFileWriter _writer;
    private readonly ConfigDirectoryLock _directoryLock;

    public NetworkSettingsStore(
        string configDirectory,
        IAtomicFileWriter? writer = null,
        ConfigDirectoryLock? directoryLock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _path = Path.Combine(Path.GetFullPath(configDirectory), FileName);
        _writer = writer ?? new AtomicFileWriter();
        _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);
    }

    public string FilePath => _path;

    /// <summary>Loads settings; missing file yields defaults. Corrupt / unsupported version throws <see cref="StoreException"/>.</summary>
    public NetworkSettings Load()
    {
        if (!File.Exists(_path))
            return new NetworkSettings();

        using (_directoryLock.Acquire())
        {
            return PortalConfigFile.WithConfigLock(() =>
            {
                try
                {
                    var file = JsonSerializer.Deserialize<NetworkFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                               ?? throw new StoreException($"'{_path}' is empty.");
                    if (file.Version != FormatVersion)
                        throw new StoreException($"'{_path}' has unsupported format version {file.Version}.");
                    return FromFile(file);
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
            });
        }
    }

    /// <summary>Loads settings, returning defaults when the file is missing or unreadable (startup-safe).</summary>
    public NetworkSettings LoadOrDefault(out Exception? error)
    {
        try
        {
            error = null;
            return Load();
        }
        catch (Exception ex) when (ex is StoreException or JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex;
            return new NetworkSettings();
        }
    }

    public void Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using (_directoryLock.Acquire())
        {
            var file = ToFile(settings);
            PortalConfigFile.WithConfigLock(() =>
            {
                var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
                _writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
                PortalConfigFile.AfterWrite(_path);
            });
        }
    }

    /// <summary>Updates a subset of fields under the config lock.</summary>
    public NetworkSettings Update(Func<NetworkSettings, NetworkSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        using (_directoryLock.Acquire())
        {
            NetworkSettings current;
            if (!File.Exists(_path))
            {
                current = new NetworkSettings();
            }
            else
            {
                current = PortalConfigFile.WithConfigLock(() =>
                {
                    var file = JsonSerializer.Deserialize<NetworkFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                               ?? throw new StoreException($"'{_path}' is empty.");
                    if (file.Version != FormatVersion)
                        throw new StoreException($"'{_path}' has unsupported format version {file.Version}.");
                    return FromFile(file);
                });
            }

            var next = mutate(current) ?? throw new InvalidOperationException("mutate returned null.");
            var payload = ToFile(next);
            PortalConfigFile.WithConfigLock(() =>
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions) + "\n";
                _writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
                PortalConfigFile.AfterWrite(_path);
            });
            return next;
        }
    }

    private static NetworkSettings FromFile(NetworkFile file) =>
        new(
            AutoCheckUpdates: file.UpdatesAutoCheck ?? true,
            LastAttemptUtc: ParseUtc(file.UpdatesLastAttemptUtc),
            ETag: EmptyToNull(file.UpdatesEtag),
            SkippedVersion: EmptyToNull(file.UpdatesSkippedVersion),
            LaterVersion: EmptyToNull(file.UpdatesLaterVersion),
            LaterUntilUtc: ParseUtc(file.UpdatesLaterUntilUtc),
            ProbeEnabled: file.StatusProbeEnabled ?? true);

    private static NetworkFile ToFile(NetworkSettings s) =>
        new()
        {
            Version = FormatVersion,
            UpdatesAutoCheck = s.AutoCheckUpdates,
            UpdatesLastAttemptUtc = FormatUtc(s.LastAttemptUtc),
            UpdatesEtag = s.ETag,
            UpdatesSkippedVersion = s.SkippedVersion,
            UpdatesLaterVersion = s.LaterVersion,
            UpdatesLaterUntilUtc = FormatUtc(s.LaterUntilUtc),
            StatusProbeEnabled = s.ProbeEnabled,
        };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
            return null;
        return dto;
    }

    private static string? FormatUtc(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private sealed class NetworkFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }

        [JsonPropertyName("updates.auto_check")] public bool? UpdatesAutoCheck { get; set; }

        [JsonPropertyName("updates.last_attempt_utc")] public string? UpdatesLastAttemptUtc { get; set; }

        [JsonPropertyName("updates.etag")] public string? UpdatesEtag { get; set; }

        [JsonPropertyName("updates.skipped_version")] public string? UpdatesSkippedVersion { get; set; }

        [JsonPropertyName("updates.later_version")] public string? UpdatesLaterVersion { get; set; }

        [JsonPropertyName("updates.later_until_utc")] public string? UpdatesLaterUntilUtc { get; set; }

        [JsonPropertyName("status.probe_enabled")] public bool? StatusProbeEnabled { get; set; }
    }
}
