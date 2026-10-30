namespace RealmPortal.Tests.Support;

/// <summary>
/// A unique, short folder directly under <c>/tmp</c> on Unix (under the temp path on Windows), deleted on dispose. For
/// socket-path tests, which must not depend on how long <c>TMPDIR</c> is.
/// </summary>
public sealed class ShortTempDirectory : IDisposable
{
    private ShortTempDirectory(string path) => Path = path;

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public static ShortTempDirectory Create()
    {
        var root = OperatingSystem.IsWindows() ? System.IO.Path.GetTempPath() : "/tmp";
        var path = System.IO.Path.Combine(root, "rp-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(path);
        return new ShortTempDirectory(path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
