using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace RealmPortal.Core.Updates;

/// <summary>
/// Downloads release assets with manual HTTPS allowlisted redirects, verifies SHA256SUMS,
/// and places files atomically (AppImage replace / Deb / WindowsZip to Downloads).
/// </summary>
public sealed class UpdateDownloader : IDisposable
{
    public const long MaxAssetBytes = 300L * 1024 * 1024;
    public const int MaxRedirectHops = 5;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Func<string> _userAgentVersion;
    private readonly Func<string, string?> _getVariable;
    private readonly Func<InstallKind> _installKind;
    private readonly Func<bool> _appImageSelfReplace;
    private readonly Func<string> _downloadsDirectory;

    public UpdateDownloader(
        HttpClient? httpClient = null,
        Func<string>? userAgentVersion = null,
        Func<string, string?>? getVariable = null,
        Func<InstallKind>? installKind = null,
        Func<bool>? appImageSelfReplace = null,
        Func<string>? downloadsDirectory = null)
    {
        _userAgentVersion = userAgentVersion ?? (() => AppInfo.Version);
        _getVariable = getVariable ?? Environment.GetEnvironmentVariable;
        _installKind = installKind ?? (() => InstallKindInfo.Current);
        _appImageSelfReplace = appImageSelfReplace ?? (() => InstallKindInfo.AppImageSelfReplace);
        _downloadsDirectory = downloadsDirectory ?? (() => XdgDownloads.GetDirectory(_getVariable));

        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                UseDefaultCredentials = false,
                CheckCertificateRevocationList = true,
            };
            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _ownsClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsClient = false;
        }
    }

    public async Task<UpdateInstallResult> DownloadAndPlaceAsync(
        ReleaseDocument release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var kind = _installKind();
        if (kind is InstallKind.Unknown)
        {
            return new UpdateInstallResult(
                SavedPath: ReleaseDocumentParser.ResolveHtmlUrl(release),
                Kind: kind,
                Instructions: "Open the release page in a browser.");
        }

        var assetName = ReleaseDocumentParser.AssetNameFor(kind, release.Version);
        var expectedUrl = ReleaseDocumentParser.ExpectedDownloadUrl(release.Version, assetName);
        var asset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.Ordinal));

        if (asset is null
            || !string.Equals(asset.BrowserDownloadUrl, expectedUrl, StringComparison.Ordinal))
        {
            // Assets list empty (cache miss path) — still allow download via expected URL when list is empty.
            if (asset is null && release.Assets.Count == 0)
            {
                asset = new ReleaseAsset(assetName, expectedUrl, Size: MaxAssetBytes);
            }
            else
            {
                throw new UpdateException(UpdateErrorCode.AssetMissing, $"Asset '{assetName}' missing or URL mismatch.");
            }
        }

        var sumsName = "SHA256SUMS";
        var sumsUrl = ReleaseDocumentParser.ExpectedDownloadUrl(release.Version, sumsName);
        var sumsBytes = await DownloadBytesAsync(
            new Uri(sumsUrl),
            pinnedSize: Sha256Sums.MaxBytes,
            absoluteDeadline: TimeSpan.FromMinutes(1),
            cancellationToken).ConfigureAwait(false);
        if (sumsBytes.Length > Sha256Sums.MaxBytes)
            throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "SHA256SUMS exceeds 64 KiB.");
        var sumsText = System.Text.Encoding.UTF8.GetString(sumsBytes);
        var expectedDigest = Sha256Sums.FindDigest(sumsText, assetName);

        var pin = asset.Size > 0 ? Math.Min(asset.Size, MaxAssetBytes) : MaxAssetBytes;

        if (kind == InstallKind.AppImage && _appImageSelfReplace()
            && AppImageTarget.TryResolve(_getVariable, out var target, out _))
        {
            return await ReplaceAppImageAsync(
                release, assetName, new Uri(expectedUrl), pin, expectedDigest, target, cancellationToken)
                .ConfigureAwait(false);
        }

        // Deb, WindowsZip, AppImage without self-replace → Downloads folder
        var downloads = _downloadsDirectory();
        PrivateStorageEnsure(downloads);
        CleanupStaleParts(downloads);

        var finalPath = XdgDownloads.AllocateNoncollidingPath(downloads, assetName);
        var partPath = PartPath(downloads, release.Version);
        try
        {
            var digest = await DownloadToPartAsync(
                new Uri(expectedUrl), partPath, pin, TimeSpan.FromMinutes(10), cancellationToken)
                .ConfigureAwait(false);
            Sha256Sums.EnsureMatches(expectedDigest, digest);

            // Final name only after verification.
            if (File.Exists(finalPath))
                finalPath = XdgDownloads.AllocateNoncollidingPath(downloads, assetName);
            File.Move(partPath, finalPath, overwrite: false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(finalPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            string? command = null;
            string? instructions = null;
            if (kind == InstallKind.Deb)
            {
                command = ShellCommandQuote.AptInstallCommand(finalPath);
                instructions = "Open the Downloads folder and install the .deb with the shown command. The app never runs sudo.";
            }
            else if (kind == InstallKind.AppImage)
            {
                command = ShellCommandQuote.ChmodUxCommand(finalPath);
                instructions = "chmod the new AppImage, start it, then delete the old one. No apt command is shown.";
            }
            else if (kind == InstallKind.WindowsZip)
            {
                instructions = "Extract the zip over your current Realm Portal folder.";
                TryWriteMotw(finalPath);
            }

            return new UpdateInstallResult(finalPath, kind, CopyableCommand: command, Instructions: instructions);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    private async Task<UpdateInstallResult> ReplaceAppImageAsync(
        ReleaseDocument release,
        string assetName,
        Uri url,
        long pin,
        byte[] expectedDigest,
        AppImageTargetInfo target,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(target.ResolvedPath)!;
        CleanupStaleParts(dir);
        var partPath = PartPath(dir, release.Version);
        try
        {
            var digest = await DownloadToPartAsync(url, partPath, pin, TimeSpan.FromMinutes(10), cancellationToken)
                .ConfigureAwait(false);
            Sha256Sums.EnsureMatches(expectedDigest, digest);

            if (!AppImageTarget.SameIdentity(target.ResolvedPath, target))
            {
                TryDelete(partPath);
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "APPIMAGE identity changed before replace.");
            }

            var mode = AppImageTarget.ModeMasked0755(target.OriginalMode);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(partPath, (UnixFileMode)mode);

            var bak = Path.Combine(dir, $".RealmPortal-{AppInfo.Version}.AppImage.bak");
            if (File.Exists(bak) || Directory.Exists(bak) || IsSymlink(bak))
            {
                if (!TryRemoveOwnedRegularFile(bak))
                {
                    TryDelete(partPath);
                    throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Existing .bak cannot be removed safely.");
                }
            }

            // Hard-link the old file as .bak, then rename .part over the target.
            try
            {
                CreateHardLink(bak, target.ResolvedPath);
            }
            catch (Exception ex)
            {
                TryDelete(partPath);
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Could not create .bak hard link: " + ex.Message, ex);
            }

            File.Move(partPath, target.ResolvedPath, overwrite: true);

            return new UpdateInstallResult(
                SavedPath: target.ResolvedPath,
                Kind: InstallKind.AppImage,
                ReplacedAppImage: true,
                BackupPath: bak,
                Instructions: "Restart now to run the new AppImage.");
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    private async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        long pinnedSize,
        TimeSpan absoluteDeadline,
        CancellationToken cancellationToken)
    {
        var partDir = Path.Combine(Path.GetTempPath(), "realm-portal-update");
        Directory.CreateDirectory(partDir);
        var part = Path.Combine(partDir, $".RealmPortal-sums.{Guid.NewGuid():N}.part");
        try
        {
            var digest = await DownloadToPartAsync(uri, part, pinnedSize, absoluteDeadline, cancellationToken)
                .ConfigureAwait(false);
            _ = digest;
            return await File.ReadAllBytesAsync(part, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(part);
        }
    }

    private async Task<byte[]> DownloadToPartAsync(
        Uri initialUri,
        string partPath,
        long pinnedSize,
        TimeSpan absoluteDeadline,
        CancellationToken cancellationToken)
    {
        TrustedHttpsUrl.EnsureAllowed(initialUri);
        var deadline = DateTimeOffset.UtcNow + absoluteDeadline;
        var uri = initialUri;

        for (var hop = 0; hop <= MaxRedirectHops; hop++)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Download deadline exceeded.")
                {
                    Host = TrustedHttpsUrl.HostOnly(uri),
                };

            TrustedHttpsUrl.EnsureAllowed(uri);
            using var request = CreateRequest(uri);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.FromSeconds(1))
                remaining = TimeSpan.FromSeconds(1);
            timeout.CancelAfter(remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10)); // connect-ish

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Download connect timed out.")
                {
                    Host = TrustedHttpsUrl.HostOnly(uri),
                };
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400)
                {
                    if (hop == MaxRedirectHops)
                        throw new UpdateException(UpdateErrorCode.UntrustedRedirect, "Too many redirects.")
                        {
                            Host = TrustedHttpsUrl.HostOnly(uri),
                        };
                    var location = response.Headers.Location
                                   ?? throw new UpdateException(UpdateErrorCode.UntrustedRedirect, "Redirect without Location.")
                                   {
                                       Host = TrustedHttpsUrl.HostOnly(uri),
                                   };
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    TrustedHttpsUrl.EnsureAllowed(uri);
                    continue;
                }

                if (status is 403 or 429)
                {
                    throw new UpdateException(UpdateErrorCode.RateLimited, $"Download rate limited ({status}).")
                    {
                        HttpStatus = status,
                        Host = TrustedHttpsUrl.HostOnly(uri),
                        RetryAfter = GitHubReleaseFeed.ParseRetryAfter(response),
                    };
                }

                if (status != 200)
                {
                    throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, $"Download HTTP {status}.")
                    {
                        HttpStatus = status,
                        Host = TrustedHttpsUrl.HostOnly(uri),
                    };
                }

                if (response.Content.Headers.ContentLength is long cl)
                {
                    if (cl > pinnedSize || cl > MaxAssetBytes)
                        throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "Content-Length exceeds limit.")
                        {
                            Host = TrustedHttpsUrl.HostOnly(uri),
                        };
                }

                var limit = Math.Min(pinnedSize, MaxAssetBytes);
                return await StreamToPartAsync(response, partPath, limit, deadline, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new UpdateException(UpdateErrorCode.UntrustedRedirect, "Redirect hop budget exhausted.");
    }

    private static async Task<byte[]> StreamToPartAsync(
        HttpResponseMessage response,
        string partPath,
        long limit,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(partPath)!;
        Directory.CreateDirectory(dir);
        TryDelete(partPath);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var file = new FileStream(partPath, options);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        var lastRead = DateTimeOffset.UtcNow;

        while (true)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Download deadline exceeded.");

            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            int read;
            try
            {
                read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), readTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Download read timed out.");
            }

            if (read == 0)
                break;
            lastRead = DateTimeOffset.UtcNow;
            total += read;
            if (total > limit)
                throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "Stream exceeded size limit.");
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sha.AppendData(buffer, 0, read);
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        file.Flush(flushToDisk: true);
        return sha.GetHashAndReset();
    }

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "RealmPortal/" + _userAgentVersion());
        request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        request.Headers.Authorization = null;
        request.Headers.Remove("Authorization");
        return request;
    }

    private static string PartPath(string directory, ReleaseVersion version) =>
        Path.Combine(directory, $".RealmPortal-{version.WithoutPrefix}.{Guid.NewGuid():N}.part");

    private static void CleanupStaleParts(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var path in Directory.EnumerateFiles(directory, ".RealmPortal-*.part"))
        {
            TryRemoveOwnedRegularFile(path);
        }
    }

    private static bool TryRemoveOwnedRegularFile(string path)
    {
        try
        {
            if (IsSymlink(path) || Directory.Exists(path))
                return false;
            if (!File.Exists(path))
                return true;
            if (!OperatingSystem.IsWindows())
            {
                if (!AppImageTarget.TryGetIdentity(path, out _, out _, out _, out var owned) || !owned)
                    return false;
            }

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private static void PrivateStorageEnsure(string directory)
    {
        try
        {
            RealmPortal.Core.IO.PrivateStorage.CreatePrivateDirectory(directory);
        }
        catch
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void TryWriteMotw(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            // Mark of the Web: Zone.Identifier alternate stream
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch
        {
            // optional
        }
    }

    /// <summary>Starts a replaced AppImage then exits the current process — called by UI after confirmation.</summary>
    public static void RestartReplacedAppImage(string targetPath, IReadOnlyList<string>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var psi = new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = false,
        };
        if (args is not null)
        {
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }

        Process.Start(psi);
    }


    private static void CreateHardLink(string linkPath, string existingFile)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkWindows(linkPath, existingFile, IntPtr.Zero))
                throw new IOException("CreateHardLink failed.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            return;
        }

        // link(oldpath, newpath): newpath becomes a hard link to oldpath.
        if (LinkUnix(existingFile, linkPath) != 0)
            throw new IOException("link(2) failed for .bak.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkWindows(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkUnix(string oldpath, string newpath);

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
