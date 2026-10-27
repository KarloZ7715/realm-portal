using System.Reflection;
using System.Text;
using Avalonia.Platform;

namespace RealmPortal.App.Platform;

/// <summary>Loads the About screen's license texts (embedded resources and optional files next to the app).</summary>
public static class AboutDocuments
{
    public const string ThirdPartyAssetPath = "avares://RealmPortal.App/Assets/THIRD_PARTY_LICENSES.md";

    private static readonly object CacheGate = new();
    private static string? _cachedThirdParty;

    /// <summary>
    /// Reads third-party license notices off the caller's thread: first from an injected stream factory (tests),
    /// then a process-wide cache, then the embedded asset, then <c>THIRD_PARTY_LICENSES.md</c> next to the app.
    /// Callers that run on the UI thread should use <see cref="LoadThirdPartyLicensesAsync"/>.
    /// </summary>
    public static string LoadThirdPartyLicenses(Func<Stream>? openEmbedded = null)
    {
        if (openEmbedded is not null)
        {
            using var injected = openEmbedded();
            using var reader = new StreamReader(injected, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        lock (CacheGate)
        {
            if (_cachedThirdParty is not null)
            {
                return _cachedThirdParty;
            }
        }

        var text = ReadThirdPartyUncached();
        lock (CacheGate)
        {
            _cachedThirdParty ??= text;
            return _cachedThirdParty;
        }
    }

    /// <summary>Loads third-party licenses on a thread-pool thread and caches the result.</summary>
    public static Task<string> LoadThirdPartyLicensesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadThirdPartyLicenses(), cancellationToken);

    /// <summary>Test helper: clears the process-wide cache.</summary>
    internal static void ClearCache()
    {
        lock (CacheGate)
        {
            _cachedThirdParty = null;
        }
    }

    private static string ReadThirdPartyUncached()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(ThirdPartyAssetPath));
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException or ArgumentException)
        {
            // Fall through to a file next to the assembly (packaged installs).
        }

        var beside = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_LICENSES.md");
        if (File.Exists(beside))
        {
            return File.ReadAllText(beside, Encoding.UTF8);
        }

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var candidate = Path.Combine(assemblyDir, "THIRD_PARTY_LICENSES.md");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate, Encoding.UTF8);
            }
        }

        return string.Empty;
    }
}
