namespace RealmPortal.Core.Updates;

/// <summary>Fetches the latest GitHub release JSON (or a test double).</summary>
public interface IReleaseFeed
{
    /// <summary>
    /// GET releases/latest. Returns the JSON body and response ETag.
    /// When <paramref name="notModified"/> is true, body may be empty and the caller must use the cache.
    /// </summary>
    Task<ReleaseFeedResponse> GetLatestAsync(string? ifNoneMatch, CancellationToken cancellationToken);
}

/// <summary>Raw feed response before version / asset interpretation.</summary>
public sealed record ReleaseFeedResponse(
    int StatusCode,
    string? ETag,
    string? Body,
    bool NotModified,
    TimeSpan? RetryAfter = null);
