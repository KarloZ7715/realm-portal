using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace RealmPortal.Core.Updates;

/// <summary>
/// HTTPS client for <c>GET /repos/KarloZ7715/realm-portal/releases/latest</c>.
/// No cookies, no credentials, no redirects, never reads GitHub tokens, never sends Authorization.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed, IDisposable
{
    public const string Owner = "KarloZ7715";
    public const string Repo = "realm-portal";
    public const string ApiVersion = "2022-11-28";
    public const int MaxBodyBytes = 1 << 20; // 1 MiB
    public static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly HttpClient _http;
    private readonly Func<string> _userAgentVersion;
    private readonly bool _ownsClient;

    public GitHubReleaseFeed(HttpClient? httpClient = null, Func<string>? userAgentVersion = null)
    {
        _userAgentVersion = userAgentVersion ?? (() => AppInfo.Version);
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                // Never use default credentials
                UseDefaultCredentials = false,
                CheckCertificateRevocationList = true,
            };
            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan, // per-request CancellationToken handles the 10 s budget
            };
            _ownsClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsClient = false;
        }
    }

    public async Task<ReleaseFeedResponse> GetLatestAsync(string? ifNoneMatch, CancellationToken cancellationToken)
    {
        TrustedHttpsUrl.EnsureAllowed(LatestReleaseUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.TryAddWithoutValidation("User-Agent", "RealmPortal/" + _userAgentVersion());
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        // Defence in depth: never send Authorization even if a custom handler injected one.
        // Tokens in the environment (GITHUB_TOKEN / GH_TOKEN) are ignored — never read for auth.
        request.Headers.Authorization = null;
        request.Headers.Remove("Authorization");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, "Update check timed out.")
            {
                Host = TrustedHttpsUrl.HostOnly(LatestReleaseUri),
            };
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var etag = response.Headers.ETag?.Tag ?? response.Headers.ETag?.ToString();

            if (status is 403 or 429)
            {
                var retry = ParseRetryAfter(response);
                throw new UpdateException(UpdateErrorCode.RateLimited, $"GitHub rate limited ({status}).")
                {
                    HttpStatus = status,
                    Host = TrustedHttpsUrl.HostOnly(LatestReleaseUri),
                    RetryAfter = retry,
                };
            }

            if (status == 304)
            {
                return new ReleaseFeedResponse(status, etag, Body: null, NotModified: true);
            }

            if (status != 200)
            {
                throw new UpdateException(UpdateErrorCode.UpdateCheckFailed, $"Unexpected HTTP status {status}.")
                {
                    HttpStatus = status,
                    Host = TrustedHttpsUrl.HostOnly(LatestReleaseUri),
                };
            }

            // Redirects are disabled; if a Location appears anyway, treat as untrusted.
            if (response.Headers.Location is not null)
            {
                throw new UpdateException(UpdateErrorCode.UntrustedRedirect, "Unexpected redirect on releases/latest.")
                {
                    HttpStatus = status,
                    Host = TrustedHttpsUrl.HostOnly(response.Headers.Location),
                };
            }

            var body = await ReadBoundedAsync(response.Content, MaxBodyBytes, timeout.Token).ConfigureAwait(false);
            return new ReleaseFeedResponse(status, etag, body, NotModified: false);
        }
    }

    internal static async Task<string> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            if (ms.Length + read > maxBytes)
                throw new UpdateException(UpdateErrorCode.DownloadTooLarge, $"Response body exceeds {maxBytes} bytes.");
            ms.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values))
        {
            var raw = values.FirstOrDefault();
            if (long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
            {
                var reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
                var wait = reset - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
        }

        return TimeSpan.FromMinutes(1);
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
