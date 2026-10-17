using PeNet;

namespace RealmPortal.Core.Clients;

/// <summary>
/// Reads the client version from <c>VS_FIXEDFILEINFO</c> in the PE version resource of <c>Wow.exe</c>
/// using PeNet. The executable is parsed, never run, so this works the same on Linux and Windows.
/// </summary>
public sealed class PeClientVersionReader : IClientVersionReader
{
    /// <summary>The client executable name, matched case-insensitively.</summary>
    public const string ExecutableName = "Wow.exe";

    public ClientVersion Read(string clientDirectory)
    {
        if (string.IsNullOrWhiteSpace(clientDirectory))
            throw new ClientVersionException(ClientVersionErrorKind.ExecutableNotFound, clientDirectory ?? string.Empty,
                "The client directory path is empty.");

        var exePath = FindExecutable(clientDirectory);
        var bytes = ReadBytes(exePath);

        PeFile? pe;
        try
        {
            if (!PeFile.TryParse(bytes, out pe) || pe is null)
                throw NotPe(exePath, null);
        }
        catch (ClientVersionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw NotPe(exePath, ex);
        }

        try
        {
            var info = pe.Resources?.VsVersionInfo?.VsFixedFileInfo;
            if (info is null)
                throw NoVersion(exePath, $"'{exePath}' has no version resource.", null);

            var ms = info.DwFileVersionMS;
            var ls = info.DwFileVersionLS;
            if (ms == 0 && ls == 0)
                throw NoVersion(exePath, $"The version resource of '{exePath}' contains version 0.0.0.0.", null);

            var version = new Version((int)(ms >> 16), (int)(ms & 0xFFFF), (int)(ls >> 16), (int)(ls & 0xFFFF));
            return new ClientVersion(version);
        }
        catch (ClientVersionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw NoVersion(exePath, $"The version resource of '{exePath}' is malformed.", ex);
        }
    }

    private static string FindExecutable(string clientDirectory)
    {
        try
        {
            if (!Directory.Exists(clientDirectory))
                throw new ClientVersionException(ClientVersionErrorKind.ExecutableNotFound, clientDirectory,
                    $"The client directory '{clientDirectory}' does not exist.");

            foreach (var file in Directory.EnumerateFiles(clientDirectory))
            {
                if (string.Equals(Path.GetFileName(file), ExecutableName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch (ClientVersionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ClientVersionException(ClientVersionErrorKind.Unreadable, clientDirectory,
                $"The client directory '{clientDirectory}' cannot be read: {ex.Message}", ex);
        }

        throw new ClientVersionException(ClientVersionErrorKind.ExecutableNotFound, clientDirectory,
            $"No {ExecutableName} found in '{clientDirectory}'.");
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ClientVersionException(ClientVersionErrorKind.Unreadable, path,
                $"'{path}' cannot be read: {ex.Message}", ex);
        }
    }

    private static ClientVersionException NotPe(string path, Exception? inner) =>
        new(ClientVersionErrorKind.NotAPortableExecutable, path, $"'{path}' is not a valid Windows executable (PE) file.", inner);

    private static ClientVersionException NoVersion(string path, string message, Exception? inner) =>
        new(ClientVersionErrorKind.NoVersionResource, path, message, inner);
}
