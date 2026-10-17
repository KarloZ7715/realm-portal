using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>Persists backup-folder and launcher settings in the config directory.</summary>
internal sealed class PortalSettingsStore(string configDirectory, IAtomicFileWriter writer, Func<string, string?> getVariable)
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

    private readonly string _storagePath = Path.Combine(configDirectory, "storage-settings.json");
    private readonly string _launcherPath = Path.Combine(configDirectory, "launcher-settings.json");

    public string DefaultBackupFolder => ClientFilesApplier.DefaultBackupRoot(getVariable);

    public string ActiveBackupFolder => LoadStorage()?.BackupFolder ?? DefaultBackupFolder;

    public StorageSettings GetStorageSettings() => new(LoadStorage()?.BackupFolder ?? DefaultBackupFolder);

    public void SetStorageSettings(StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateBackupFolder(settings.BackupFolder);
        var file = new StorageFile { Version = FormatVersion, BackupFolder = Path.GetFullPath(settings.BackupFolder) };
        Write(_storagePath, file);
    }

    public LauncherSettings GetLauncherSettings()
    {
        if (!File.Exists(_launcherPath))
            return new LauncherSettings();

        try
        {
            var file = JsonSerializer.Deserialize<LauncherFile>(File.ReadAllBytes(_launcherPath), JsonOptions)
                       ?? throw new StoreException($"'{_launcherPath}' is empty.");
            if (file.Version != FormatVersion)
                throw new StoreException($"'{_launcherPath}' has unsupported format version {file.Version}.");
            return new LauncherSettings(file.CommandTemplate, file.Arguments, file.Environment, file.Prefix);
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{_launcherPath}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{_launcherPath}' cannot be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"'{_launcherPath}' cannot be read: {ex.Message}", ex);
        }
    }

    public void SetLauncherSettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = settings.ToSpec(Path.Combine(Path.GetTempPath(), "Wow.exe"), Path.GetTempPath()).Resolve();
        var file = new LauncherFile
        {
            Version = FormatVersion,
            CommandTemplate = settings.CommandTemplate,
            Arguments = settings.Arguments?.ToList(),
            Environment = settings.Environment is null ? null : new Dictionary<string, string?>(settings.Environment),
            Prefix = settings.Prefix?.ToList(),
        };
        Write(_launcherPath, file);
    }

    public static void ValidateBackupFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, full, $"Backup folder '{full}' does not exist.");

        var probe = Path.Combine(full, ".realm-portal-write-test");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, full, ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.ReadOnlyFs, full, ex.Message, ex);
        }
    }

    private StorageFile? LoadStorage()
    {
        if (!File.Exists(_storagePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<StorageFile>(File.ReadAllBytes(_storagePath), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{_storagePath}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{_storagePath}' cannot be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"'{_storagePath}' cannot be read: {ex.Message}", ex);
        }
    }

    private void Write<T>(string path, T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions) + "\n";
        writer.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(json));
    }

    private sealed class StorageFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("backup_folder")] public string? BackupFolder { get; set; }
    }

    private sealed class LauncherFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("command_template")] public string? CommandTemplate { get; init; }
        [JsonPropertyName("arguments")] public List<string>? Arguments { get; init; }
        [JsonPropertyName("environment")] public Dictionary<string, string?>? Environment { get; init; }
        [JsonPropertyName("prefix")] public List<string>? Prefix { get; init; }
    }
}
