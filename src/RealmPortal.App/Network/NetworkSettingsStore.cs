using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.App.Network;

/// <summary>
/// App-layer adapter for <c>network-settings.json</c> until Core's <c>NetworkSettingsStore</c> lands.
/// Same file layout, version 1, mode 0600, bounded reads, <see cref="ConfigDirectoryLock"/>.
/// </summary>
public sealed class NetworkSettingsStore : INetworkSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly string _configDirectory;
    private readonly IAtomicFileWriter _writer;
    private readonly ConfigDirectoryLock _directoryLock;

    public NetworkSettingsStore(string configDirectory, IAtomicFileWriter? writer = null, ConfigDirectoryLock? directoryLock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
        _path = Path.Combine(configDirectory, "network-settings.json");
        _writer = writer ?? new AtomicFileWriter();
        _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);
    }

    public NetworkSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new NetworkSettings();

            using var bounded = new MemoryStream(PrivateStorage.ReadBounded(_path, PrivateStorage.MaxJsonBytes));
            var loaded = JsonSerializer.Deserialize<NetworkSettings>(bounded, JsonOptions) ?? new NetworkSettings();
            if (loaded.Version != NetworkSettings.FormatVersion)
                return new NetworkSettings();
            if (loaded.UpdatesLastAttemptUtc is { } last && last > DateTimeOffset.UtcNow)
                loaded.UpdatesLastAttemptUtc = DateTimeOffset.UtcNow;
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or StoreException or FormatException)
        {
            return new NetworkSettings();
        }
    }

    public void Save(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Version = NetworkSettings.FormatVersion;
        using (_directoryLock.Acquire())
        {
            var existed = Directory.Exists(_configDirectory);
            var directory = OperatingSystem.IsWindows()
                ? Directory.CreateDirectory(_configDirectory)
                : Directory.CreateDirectory(
                    _configDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!existed && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, JsonOptions));
            _writer.WriteAllBytes(
                _path,
                bytes,
                new FileReplacementPermissions((int)(UnixFileMode.UserRead | UnixFileMode.UserWrite), null));
        }
    }
}
