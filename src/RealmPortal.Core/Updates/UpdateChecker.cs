namespace RealmPortal.Core.Updates;

/// <summary>Orchestrates policy, feed fetch, version comparison and postpone/skip.</summary>
public sealed class UpdateChecker : IUpdateChecker
{
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan ManualDebounce = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan LaterDuration = TimeSpan.FromDays(3);

    private readonly NetworkSettingsStore _settings;
    private readonly UpdateCacheStore _cache;
    private readonly IReleaseFeed _feed;
    private readonly UpdateDownloader _downloader;
    private readonly Func<string> _runningVersion;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private DateTimeOffset? _lastManualAttemptUtc;
    private DateTimeOffset? _rateLimitedUntilUtc;

    public UpdateChecker(
        NetworkSettingsStore settings,
        UpdateCacheStore cache,
        IReleaseFeed feed,
        UpdateDownloader? downloader = null,
        Func<string>? runningVersion = null,
        Func<DateTimeOffset>? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _downloader = downloader ?? new UpdateDownloader();
        _runningVersion = runningVersion ?? (() => AppInfo.Version);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public NetworkSettingsStore Settings => _settings;

    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckMode mode, CancellationToken cancellationToken = default)
    {
        var now = _clock();
        NetworkSettings settings;
        try
        {
            settings = _settings.Load();
        }
        catch (Exception ex)
        {
            return Fail(UpdateErrorCode.UpdateCheckFailed, ex.Message);
        }

        if (mode == UpdateCheckMode.Automatic)
        {
            if (!settings.AutoCheckUpdates)
                return new UpdateCheckResult(UpdateCheckOutcome.SkippedByPolicy);

            var last = NormalizeAttempt(settings.LastAttemptUtc, now);
            if (last is not null && now - last.Value < AutoCheckInterval)
                return new UpdateCheckResult(UpdateCheckOutcome.SkippedByPolicy);

            // Write attempt time at the start so a failed check also waits 24 h.
            try
            {
                _settings.Update(s => s with { LastAttemptUtc = now });
            }
            catch (Exception ex)
            {
                return Fail(UpdateErrorCode.UpdateCheckFailed, ex.Message);
            }
        }
        else
        {
            lock (_gate)
            {
                if (_lastManualAttemptUtc is DateTimeOffset prev && now - prev < ManualDebounce)
                    return new UpdateCheckResult(UpdateCheckOutcome.SkippedByPolicy, Diagnostic: "Manual check debounced.");
                _lastManualAttemptUtc = now;
            }
        }

        lock (_gate)
        {
            if (_rateLimitedUntilUtc is DateTimeOffset until && now < until)
            {
                return new UpdateCheckResult(
                    UpdateCheckOutcome.RateLimited,
                    ErrorCode: UpdateErrorCode.RateLimited,
                    Diagnostic: "Rate limited until " + until.UtcDateTime.ToString("o"));
            }
        }

        string? ifNoneMatch = null;
        var hasCache = _cache.TryLoad(out var cachedEtag, out var cachedBody);
        if (hasCache && !string.IsNullOrWhiteSpace(settings.ETag) && settings.ETag == cachedEtag)
            ifNoneMatch = cachedEtag;
        else if (!hasCache && !string.IsNullOrWhiteSpace(settings.ETag))
        {
            // Cache missing: drop ETag and make an unconditional request.
            try { _settings.Update(s => s with { ETag = null }); } catch { /* best effort */ }
            settings = settings with { ETag = null };
        }

        ReleaseFeedResponse response;
        try
        {
            response = await _feed.GetLatestAsync(ifNoneMatch, cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateException ex) when (ex.Code == UpdateErrorCode.RateLimited)
        {
            var until = now + (ex.RetryAfter ?? TimeSpan.FromMinutes(1));
            lock (_gate) { _rateLimitedUntilUtc = until; }
            return new UpdateCheckResult(UpdateCheckOutcome.RateLimited, ErrorCode: ex.Code, Diagnostic: ex.Message, HttpStatus: ex.HttpStatus);
        }
        catch (UpdateException ex)
        {
            return Fail(ex.Code, ex.Message, ex.HttpStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(UpdateErrorCode.UpdateCheckFailed, ex.Message);
        }

        string body;
        if (response.NotModified)
        {
            if (!hasCache)
            {
                // 304 without a body we can use — drop ETag and treat as failure for this attempt.
                try { _settings.Update(s => s with { ETag = null }); } catch { /* ignore */ }
                return Fail(UpdateErrorCode.UpdateCheckFailed, "304 without cached body.");
            }

            body = cachedBody;
        }
        else
        {
            body = response.Body ?? "";
            if (!string.IsNullOrWhiteSpace(response.ETag))
            {
                try
                {
                    _cache.Save(response.ETag, body);
                    _settings.Update(s => s with { ETag = response.ETag });
                    settings = settings with { ETag = response.ETag };
                }
                catch (Exception ex)
                {
                    // Cache write failure must not block version comparison.
                    _ = ex;
                }
            }
        }

        return Evaluate(body, settings, now);
    }

    public void RemindLater(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!ReleaseVersion.TryParseInformational(version, out var parsed)
            && !ReleaseVersion.TryParseTag(version.StartsWith('v') ? version : "v" + version, out parsed))
            throw new ArgumentException("Version is not a valid release version.", nameof(version));

        var until = _clock() + LaterDuration;
        _settings.Update(s => s with
        {
            LaterVersion = parsed.WithoutPrefix,
            LaterUntilUtc = until,
        });
    }

    public void SkipThisVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!ReleaseVersion.TryParseInformational(version, out var parsed)
            && !ReleaseVersion.TryParseTag(version.StartsWith('v') ? version : "v" + version, out parsed))
            throw new ArgumentException("Version is not a valid release version.", nameof(version));

        _settings.Update(s => s with { SkippedVersion = parsed.WithoutPrefix });
    }

    public Task<UpdateInstallResult> InstallUpdateAsync(UpdateOffer offer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (!ReleaseVersion.TryParseInformational(offer.Version, out var version)
            && !ReleaseVersion.TryParseTag(offer.TagName ?? ("v" + offer.Version), out version))
            throw new UpdateException(UpdateErrorCode.AssetMissing, "Offer version is not parseable.");

        // Re-fetch body from cache for assets; Install uses the feed URL builder + SHA256SUMS download.
        string? body = null;
        if (_cache.TryLoad(out _, out var cached))
            body = cached;

        ReleaseDocument? doc = body is null ? null : ReleaseDocumentParser.TryParse(body);
        if (doc is null || doc.Version != version)
        {
            // Build a minimal document so the downloader can still use ExpectedDownloadUrl.
            doc = new ReleaseDocument(version, version.TagName, offer.ReleaseNotesPlain, offer.HtmlUrl, []);
        }

        return _downloader.DownloadAndPlaceAsync(doc, cancellationToken);
    }

    private UpdateCheckResult Evaluate(string body, NetworkSettings settings, DateTimeOffset now)
    {
        var release = ReleaseDocumentParser.TryParse(body);
        if (release is null)
            return new UpdateCheckResult(UpdateCheckOutcome.NoNotice, Diagnostic: "Release not eligible (draft/prerelease/bad tag).");

        if (!ReleaseVersion.TryParseInformational(_runningVersion(), out var running))
            return new UpdateCheckResult(UpdateCheckOutcome.NoNotice, Diagnostic: "Running version is not SemVer.");

        if (release.Version <= running)
            return new UpdateCheckResult(UpdateCheckOutcome.UpToDate);

        if (settings.SkippedVersion is { } skipped
            && ReleaseVersion.TryParseInformational(skipped, out var skippedVer)
            && skippedVer == release.Version)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NoNotice, Diagnostic: "Version skipped by user.");
        }

        if (settings.LaterVersion is { } later
            && settings.LaterUntilUtc is { } until
            && ReleaseVersion.TryParseInformational(later, out var laterVer)
            && laterVer == release.Version)
        {
            var effectiveUntil = until > now.AddYears(10) ? now : until; // future absurd values: still compare
            if (until > now)
                return new UpdateCheckResult(UpdateCheckOutcome.NoNotice, Diagnostic: "Version postponed (Later).");
            // Past until → offer again
            _ = effectiveUntil;
        }

        var offer = new UpdateOffer(
            Version: release.Version.WithoutPrefix,
            TagName: release.TagName,
            ReleaseNotesPlain: release.ReleaseNotesPlain,
            HtmlUrl: ReleaseDocumentParser.ResolveHtmlUrl(release));

        return new UpdateCheckResult(UpdateCheckOutcome.Offer, Offer: offer);
    }

    private static DateTimeOffset? NormalizeAttempt(DateTimeOffset? last, DateTimeOffset now)
    {
        if (last is null)
            return null;
        // A value in the future is treated as "now".
        return last.Value > now ? now : last.Value;
    }

    private static UpdateCheckResult Fail(UpdateErrorCode code, string diagnostic, int? status = null) =>
        new(UpdateCheckOutcome.Failed, ErrorCode: code, Diagnostic: diagnostic, HttpStatus: status);
}
