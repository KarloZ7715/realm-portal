using System.Reflection;
using System.Text;
using Avalonia.Platform;

namespace RealmPortal.App.Platform;

/// <summary>Loads the About screen's license texts (embedded resources and optional files next to the app).</summary>
public static class AboutDocuments
{
    public const string ThirdPartyAssetPath = "avares://RealmPortal.App/Assets/THIRD_PARTY_LICENSES.md";

    /// <summary>
    /// Reads third-party license notices: first from an injected stream factory (tests), then the embedded asset,
    /// then <c>THIRD_PARTY_LICENSES.md</c> next to the entry assembly (release packages).
    /// </summary>
    public static string LoadThirdPartyLicenses(Func<Stream>? openEmbedded = null)
    {
        if (openEmbedded is not null)
        {
            using var injected = openEmbedded();
            using var reader = new StreamReader(injected, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

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
