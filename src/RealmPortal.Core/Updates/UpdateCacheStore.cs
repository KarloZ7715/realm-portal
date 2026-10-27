using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;

namespace RealmPortal.Core.Updates;

/// <summary>
/// Persists the last successful <c>releases/latest</c> body with its ETag (<c>update-cache.json</c>),
/// same storage rules as network-settings (bounded, 0600, config lock).
/// </summary>
public sealed class UpdateCacheStore
{
    public const string FileName = "update-cache.json";
    public const int FormatVersion = 1;
    public const int MaxBodyBytes = 1 << 20; // 1 MiB — same as the live response cap

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly IAtomicFileWriter _writer;
    private readonly ConfigDirectoryLock _directoryLock;

    public UpdateCacheStore(
        string configDirectory,
        IAtomicFileWriter? writer = null,
        ConfigDirectoryLock? directoryLock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _path = Path.Combine(Path.GetFullPath(configDirectory), FileName);
        _writer = writer ?? new AtomicFileWriter();
        _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);
    }

    public bool TryLoad(out string etag, out string body)
    {
        etag = "";
        body = "";
        if (!File.Exists(_path))
            return false;

        try
        {
            using (_directoryLock.Acquire())
            {
                string? loadedEtag = null;
                string? loadedBody = null;
                var ok = PortalConfigFile.WithConfigLock(() =>
                {
                    var file = JsonSerializer.Deserialize<CacheFile>(PortalConfigFile.ReadBounded(_path), JsonOptions);
                    if (file is null || file.Version != FormatVersion)
                        return false;
                    if (string.IsNullOrWhiteSpace(file.ETag) || string.IsNullOrEmpty(file.Body))
                        return false;
                    if (System.Text.Encoding.UTF8.GetByteCount(file.Body) > MaxBodyBytes)
                        return false;
                    loadedEtag = file.ETag;
                    loadedBody = file.Body;
                    return true;
                });
                if (!ok || loadedEtag is null || loadedBody is null)
                    return false;
                etag = loadedEtag;
                body = loadedBody;
                return true;
            }
        }
        catch (Exception ex) when (ex is StoreException or JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Save(string etag, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        ArgumentNullException.ThrowIfNull(body);
        if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
            throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "Cached release body exceeds 1 MiB.");

        using (_directoryLock.Acquire())
        {
            var file = new CacheFile { Version = FormatVersion, ETag = etag, Body = body };
            PortalConfigFile.WithConfigLock(() =>
            {
                var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
                _writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
                PortalConfigFile.AfterWrite(_path);
            });
        }
    }

    public void Clear()
    {
        using (_directoryLock.Acquire())
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private sealed class CacheFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("etag")] public string? ETag { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
    }
}
