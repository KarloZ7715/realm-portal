using System.Net.Http.Headers;
using System.Security.Cryptography;
using RealmPortal.Core.Updates.Signing;

namespace RealmPortal.Core.Updates;

/// <summary>
/// Downloads release assets with manual HTTPS allowlisted redirects, checks the minisign signature on SHA256SUMS,
/// verifies the asset against it, and places files atomically (AppImage replace / Deb / WindowsZip to Downloads).
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
    private readonly IReleaseSignatureVerifier _signatureVerifier;

    public UpdateDownloader(
        HttpClient? httpClient = null,
        Func<string>? userAgentVersion = null,
        Func<string, string?>? getVariable = null,
        Func<InstallKind>? installKind = null,
        Func<bool>? appImageSelfReplace = null,
        Func<string>? downloadsDirectory = null,
        IReleaseSignatureVerifier? signatureVerifier = null)
    {
        _signatureVerifier = signatureVerifier ?? MinisignVerifier.ForRelease();
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

    /// <summary>The verifier in use; tests check that the default is the built-in release key.</summary>
    internal IReleaseSignatureVerifier SignatureVerifier => _signatureVerifier;

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

        var sumsText = await DownloadVerifiedSumsAsync(release, cancellationToken).ConfigureAwait(false);
        var expectedDigest = Sha256Sums.FindDigest(sumsText, assetName);

        var pin = asset.Size > 0 ? Math.Min(asset.Size, MaxAssetBytes) : MaxAssetBytes;

        if (kind == InstallKind.AppImage && _appImageSelfReplace()
            && AppImageTarget.TryResolve(_getVariable, out var target, out _))
        {
            return await ReplaceAppImageAsync(
                release, assetName, new Uri(expectedUrl), pin, expectedDigest, target, cancellationToken)
                .ConfigureAwait(false);
        }

        // Deb, WindowsZip, AppImage without self-replace: Downloads if that folder (and its ancestors) are
        // safe; otherwise $XDG_CACHE_HOME/RealmPortal/updates; otherwise the release page (#206).
        if (!TryResolvePlacementDirectory(release, out var downloads, out var placementFallback))
            return placementFallback!;

        CleanupStaleParts(downloads);
        CleanupStaleStaging(downloads);

        // Written and verified inside a fresh private (0700) directory next to the final name, so no other
        // user can swap the verified bytes before they get their final name (#206). Same filesystem: rename only.
        var staging = CreateStagingDirectory(downloads);
        var partPath = PartPath(staging, release.Version);
        string finalPath;
        try
        {
            var digest = await DownloadToPartAsync(
                new Uri(expectedUrl), partPath, pin, TimeSpan.FromMinutes(10), cancellationToken)
                .ConfigureAwait(false);
            Sha256Sums.EnsureMatches(expectedDigest, digest);

            // Final name only after verification; never overwrite an existing file (renameat2 RENAME_NOREPLACE).
            finalPath = XdgDownloads.AllocateNoncollidingPath(downloads, assetName);
            if (!TryMoveNoReplace(partPath, ref finalPath, assetName, downloads, out var moveReason))
                return UnsafeTargetFallback(release, moveReason);
            FsyncDirectory(downloads);
        }
        finally
        {
            TryDeleteStaging(staging);
        }

        string? command = null;
        string? instructions = null;
        if (kind == InstallKind.Deb)
        {
            command = ShellCommandQuote.AptInstallCommand(finalPath);
            instructions = "Install the .deb with the command shown; the app never runs sudo.";
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

    /// <summary>
    /// Downloads <c>SHA256SUMS</c> and <c>SHA256SUMS.minisig</c> from the release and returns the checksums text only
    /// when the signature is valid for this release's tag. There is no unsigned fallback.
    /// </summary>
    public async Task<string> DownloadVerifiedSumsAsync(
        ReleaseDocument release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        // A release whose asset list we have and that lists no signature is unsigned; don't bother downloading.
        if (release.Assets.Count > 0
            && !release.Assets.Any(a => string.Equals(a.Name, ReleaseSignature.AssetName, StringComparison.Ordinal)))
        {
            throw new UpdateException(UpdateErrorCode.SignatureMissing, "The release has no SHA256SUMS.minisig.");
        }

        var sumsBytes = await DownloadBytesAsync(
            new Uri(ReleaseDocumentParser.ExpectedDownloadUrl(release.Version, "SHA256SUMS")),
            pinnedSize: Sha256Sums.MaxBytes,
            absoluteDeadline: TimeSpan.FromMinutes(1),
            cancellationToken).ConfigureAwait(false);
        if (sumsBytes.Length > Sha256Sums.MaxBytes)
            throw new UpdateException(UpdateErrorCode.DownloadTooLarge, "SHA256SUMS exceeds 64 KiB.");

        byte[] signature;
        try
        {
            // The pin makes the stream copy stop at 16 KiB, and a bigger Content-Length is refused before reading.
            signature = await DownloadBytesAsync(
                new Uri(ReleaseDocumentParser.ExpectedDownloadUrl(release.Version, ReleaseSignature.AssetName)),
                pinnedSize: SignedSha256Sums.MaxSignatureBytes,
                absoluteDeadline: TimeSpan.FromMinutes(1),
                cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateException ex) when (ex.HttpStatus == 404)
        {
            throw new UpdateException(UpdateErrorCode.SignatureMissing, "The release has no SHA256SUMS.minisig.", ex)
            {
                HttpStatus = ex.HttpStatus,
                Host = ex.Host,
            };
        }
        catch (UpdateException ex) when (ex.Code == UpdateErrorCode.DownloadTooLarge)
        {
            throw new UpdateException(UpdateErrorCode.SignatureInvalid, "SHA256SUMS.minisig exceeds 16 KiB.", ex)
            {
                Signature = SignatureStatus.TooLarge,
            };
        }

        return SignedSha256Sums.EnsureValid(_signatureVerifier, release.TagName, sumsBytes, signature);
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

        // Fail closed (#206): if another user can write the directory or the AppImage, they could swap the file between
        // verification and the replace. Do not replace in place; hand back the release page instead.
        if (!IsSafeToReplace(target, dir, out var unsafeReason))
            return UnsafeTargetFallback(release, unsafeReason);

        CleanupStaleParts(dir);
        CleanupStaleStaging(dir);

        // Download and verify inside a fresh 0700 directory next to the AppImage (same filesystem, so the final step is
        // one rename(2)). Only the current user can enter it, so the verified bytes cannot be swapped by path.
        var staging = CreateStagingDirectory(dir);
        var partPath = PartPath(staging, release.Version);
        try
        {
            var digest = await DownloadToPartAsync(url, partPath, pin, TimeSpan.FromMinutes(10), cancellationToken)
                .ConfigureAwait(false);
            Sha256Sums.EnsureMatches(expectedDigest, digest);

            var mode = AppImageTarget.ModeMasked0755(target.OriginalMode);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(partPath, (UnixFileMode)mode);

            // Re-check right before the replace: same file as resolved, and still nobody else can write there.
            if (!AppImageTarget.SameIdentity(target.ResolvedPath, target))
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "APPIMAGE identity changed before replace.");
            if (!IsSafeToReplace(target, dir, out unsafeReason))
                return UnsafeTargetFallback(release, unsafeReason);

            // Random suffix so a 1777 directory cannot be DoS'd by planting a predictable .bak name (#206).
            var bak = Path.Combine(dir, $".RealmPortal-{AppInfo.Version}.{Guid.NewGuid():N}.AppImage.bak");
            if (File.Exists(bak) || Directory.Exists(bak) || IsSymlink(bak))
                return UnsafeTargetFallback(release, "Backup name collided.");

            // Hard-link the old file as .bak, then rename(2) the verified file over the target. Any errno
            // (including EXDEV, which would otherwise copy-and-truncate the shared inode) falls back.
            if (!UnixPathSafety.TryHardLink(target.ResolvedPath, bak, out var linkErrno))
                return UnsafeTargetFallback(release, "Could not create the .bak hard link (errno " + linkErrno + ").");
            if (!UnixPathSafety.TryRenameOverwrite(partPath, target.ResolvedPath, out var renameErrno))
            {
                TryDelete(bak);
                return UnsafeTargetFallback(release, "Could not replace the AppImage (errno " + renameErrno + ").");
            }

            FsyncDirectory(dir);

            return new UpdateInstallResult(
                SavedPath: target.ResolvedPath,
                Kind: InstallKind.AppImage,
                ReplacedAppImage: true,
                BackupPath: bak,
                Instructions: "Restart now to run the new AppImage.");
        }
        finally
        {
            TryDeleteStaging(staging);
        }
    }

    private static bool IsSafeToReplace(AppImageTargetInfo target, string dir, out string? reason) =>
        AppImageTarget.IsDirectorySafeForReplace(dir, out reason)
        && AppImageTarget.IsFileSafeForReplace(target.ResolvedPath, out reason);

    /// <summary>
    /// The AppImage could be changed by another user, so nothing is replaced: same result as an <see cref="InstallKind.Unknown"/>
    /// build (the app opens the release page). The downloaded bytes, if any, are already gone with the staging directory.
    /// </summary>
    private static UpdateInstallResult UnsafeTargetFallback(ReleaseDocument release, string? reason) =>
        new(
            SavedPath: ReleaseDocumentParser.ResolveHtmlUrl(release),
            Kind: InstallKind.Unknown,
            Instructions: "The destination can be changed by other users, so the update was not installed ("
                          + (reason ?? "unsafe target") + "). Open the release page in a browser.");

    private const string StagingPrefix = ".RealmPortal-staging-";

    /// <summary>Creates a new directory only the current user can use (0700), next to the final file.</summary>
    private static string CreateStagingDirectory(string parent)
    {
        var path = Path.Combine(parent, StagingPrefix + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path))
            throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Staging directory already exists.");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return path;
        }

        Directory.CreateDirectory(path, RealmPortal.Core.IO.PrivateStorage.PrivateDirectoryMode);
        // CreateDirectory succeeds on an existing directory, and the mode is subject to umask: check what we got.
        if (!UnixPathSafety.TryLstat(path, out var st) || !st.IsDirectory || !st.OwnedByUser)
            throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Staging directory is not private.");

        if ((st.Permissions & 0x1FF) != 0b111_000_000)
            File.SetUnixFileMode(path, RealmPortal.Core.IO.PrivateStorage.PrivateDirectoryMode);
        return path;
    }

    private static void TryDeleteStaging(string path)
    {
        // Never rm -rf: delete only user-owned regular *.part files, then rmdir. Skip if the parent is unsafe.
        UnixPathSafety.TryDeleteOwnedFilesThenRmdir(path, ".RealmPortal-*.part");
    }

    /// <summary>Removes staging directories left by a crash; only when the parent directory is safe.</summary>
    private static void CleanupStaleStaging(string directory)
    {
        if (!Directory.Exists(directory) || !IsSafeDestination(directory))
            return;
        foreach (var path in Directory.EnumerateDirectories(directory, StagingPrefix + "*"))
            TryDeleteStaging(path);
    }

    /// <summary>fsync(2) on a directory so a rename into it survives a crash. Best effort; no-op on Windows.</summary>
    private static void FsyncDirectory(string directory) => UnixPathSafety.FsyncDirectory(directory);

    private bool TryResolvePlacementDirectory(
        ReleaseDocument release, out string directory, out UpdateInstallResult? fallback)
    {
        directory = null!;
        fallback = null;
        var downloads = _downloadsDirectory();
        PrivateStorageEnsure(downloads);
        if (IsSafeDestination(downloads))
        {
            directory = downloads;
            return true;
        }

        var cache = GetPrivateUpdatesCache();
        try
        {
            PrivateStorageEnsure(cache);
        }
        catch (Exception ex)
        {
            fallback = UnsafeTargetFallback(release, "Could not create the private updates cache: " + ex.Message);
            return false;
        }

        if (IsSafeDestination(cache))
        {
            directory = cache;
            return true;
        }

        fallback = UnsafeTargetFallback(
            release, "The Downloads folder and the private updates cache can be changed by other users.");
        return false;
    }

    private string GetPrivateUpdatesCache()
    {
        var cacheHome = _getVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome))
        {
            var home = _getVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cacheHome = Path.Combine(home, ".cache");
        }

        return Path.Combine(Path.GetFullPath(cacheHome), "RealmPortal", "updates");
    }

    private static bool IsSafeDestination(string directory) =>
        OperatingSystem.IsWindows() || UnixPathSafety.IsDirectoryTreeSafe(directory, out _);

    /// <summary>Atomic no-overwrite move via renameat2(RENAME_NOREPLACE), or link+unlink. Retries on EEXIST.</summary>
    private static bool TryMoveNoReplace(
        string partPath, ref string finalPath, string assetName, string destDir, out string? reason)
    {
        reason = null;
        if (OperatingSystem.IsWindows())
        {
            File.Move(partPath, finalPath, overwrite: false);
            return true;
        }

        for (var i = 0; i < 10_000; i++)
        {
            if (UnixPathSafety.TryRenameNoReplace(partPath, finalPath, out var errno))
                return true;
            if (errno == UnixPathSafety.ErrnoExist)
            {
                finalPath = XdgDownloads.AllocateNoncollidingPath(destDir, assetName);
                continue;
            }

            reason = "Could not move the verified file into place (errno " + errno + ").";
            return false;
        }

        reason = "Could not allocate a unique download path.";
        return false;
    }

    private async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        long pinnedSize,
        TimeSpan absoluteDeadline,
        CancellationToken cancellationToken)
    {
        // A fresh private temp directory (0700 on Unix), never a shared fixed name under /tmp (#206).
        var partDir = Directory.CreateTempSubdirectory("realm-portal-update-").FullName;
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
            TryDeleteStaging(partDir);
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
        if (!Directory.Exists(directory) || !IsSafeDestination(directory))
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
            // File.Exists is false for a symlink to a directory, so do not use it as a guard (#206).
            return new FileInfo(path).LinkTarget is not null
                   || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
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

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
