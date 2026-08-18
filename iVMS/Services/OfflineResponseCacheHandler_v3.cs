using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace V3SClient.Services
{
    /// <summary>
    /// Keeps the last successful GET response available when an API becomes
    /// temporarily unavailable. Mutating requests are never cached.
    /// </summary>
    public sealed class OfflineResponseCacheHandler_v3 : DelegatingHandler
    {
        private sealed class CachedResponse
        {
            public DateTime StoredAtUtc { get; set; }
            public HttpStatusCode StatusCode { get; set; }
            public byte[] Body { get; set; }
            public string MediaType { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CachedResponse> Cache =
            new ConcurrentDictionary<string, CachedResponse>(StringComparer.Ordinal);

        private static readonly TimeSpan MaximumStaleAge = TimeSpan.FromMinutes(30);
        private const int MaximumEntries = 512;
        private const long MaximumBytes = 32L * 1024L * 1024L;
        private static long CachedBytes;
        private static long CacheHits;
        private static long CacheMisses;

        public OfflineResponseCacheHandler_v3(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public static void Clear()
        {
            Cache.Clear();
            Interlocked.Exchange(ref CachedBytes, 0);
        }

        public static string GetStatistics()
        {
            return string.Format("entries={0}, bytes={1}, hits={2}, misses={3}",
                Cache.Count, Interlocked.Read(ref CachedBytes),
                Interlocked.Read(ref CacheHits), Interlocked.Read(ref CacheMisses));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null || request.Method != HttpMethod.Get)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var freshFor = GetFreshCacheDuration(request.RequestUri);
            var key = BuildCacheKey(request);
            if (freshFor > TimeSpan.Zero && TryCreateFreshResponse(key, request, freshFor, out var fresh))
                return fresh;

            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    Store(key, new CachedResponse
                    {
                        StoredAtUtc = DateTime.UtcNow,
                        StatusCode = response.StatusCode,
                        Body = body,
                        MediaType = mediaType
                    });
                    response.Content.Dispose();
                    response.Content = new ByteArrayContent(body);
                    CopyContentType(response.Content, mediaType);
                }
                else if (IsTransientStatus(response.StatusCode) && TryCreateFallback(key, request, out var fallback))
                {
                    response.Dispose();
                    return fallback;
                }
                return response;
            }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
            {
                HttpResponseMessage fallback;
                if (!TryCreateFallback(key, request, out fallback))
                    throw;
                return fallback;
            }
        }

        private static bool TryCreateFreshResponse(string key, HttpRequestMessage request,
            TimeSpan maxAge, out HttpResponseMessage response)
        {
            if (Cache.TryGetValue(key, out var cached) &&
                DateTime.UtcNow - cached.StoredAtUtc <= maxAge)
            {
                Interlocked.Increment(ref CacheHits);
                response = CreateResponse(cached, request);
                return true;
            }

            Interlocked.Increment(ref CacheMisses);
            response = null;
            return false;
        }

        private static HttpResponseMessage CreateResponse(CachedResponse cached, HttpRequestMessage request)
        {
            var response = new HttpResponseMessage(cached.StatusCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(cached.Body ?? new byte[0])
            };
            CopyContentType(response.Content, cached.MediaType);
            response.Headers.TryAddWithoutValidation("X-iVMS-Cache", "fresh");
            return response;
        }

        private static void Store(string key, CachedResponse value)
        {
            if (value == null || value.Body == null || value.Body.LongLength > MaximumBytes)
                return;

            if (Cache.TryGetValue(key, out var previous))
                Interlocked.Add(ref CachedBytes, -previous.Body.LongLength);

            Cache[key] = value;
            Interlocked.Add(ref CachedBytes, value.Body.LongLength);

            // Eviction is intentionally coarse and deterministic: API caches are
            // small, bounded, and can be rebuilt from the server at any time.
            if (Cache.Count > MaximumEntries || Interlocked.Read(ref CachedBytes) > MaximumBytes)
                Clear();
        }

        private static TimeSpan GetFreshCacheDuration(Uri uri)
        {
            if (uri == null) return TimeSpan.Zero;
            var value = uri.AbsoluteUri.ToLowerInvariant();
            var query = uri.Query.ToLowerInvariant();
            if (value.Contains("/status") || value.Contains("/live") ||
                value.Contains("/stream") || value.Contains("/playback") ||
                value.Contains("/events") || value.Contains("/ai/") ||
                value.Contains("/metrics") || query.Contains("start") ||
                query.Contains("end") || query.Contains("timestamp") ||
                query.Contains("page"))
                return TimeSpan.Zero;

            if (value.Contains("/cameras/groups")) return TimeSpan.FromMinutes(2);
            if (value.Contains("/cameras") || value.Contains("/config") ||
                value.Contains("/settings") || value.Contains("/endpoint"))
                return TimeSpan.FromMinutes(1);

            return TimeSpan.Zero;
        }

        private static bool TryCreateFallback(string key, HttpRequestMessage request, out HttpResponseMessage fallback)
        {
            CachedResponse cached;
            if (!Cache.TryGetValue(key, out cached) ||
                DateTime.UtcNow - cached.StoredAtUtc > MaximumStaleAge)
            {
                fallback = null;
                return false;
            }

            fallback = CreateResponse(cached, request);
            fallback.Headers.Remove("X-iVMS-Cache");
            fallback.Headers.TryAddWithoutValidation("X-iVMS-Cache", "stale");
            return true;
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == (HttpStatusCode)429 ||
                   (int)statusCode >= 500;
        }

        private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;
            return exception is HttpRequestException || exception is TaskCanceledException || exception is TimeoutException;
        }

        private static string BuildCacheKey(HttpRequestMessage request)
        {
            var token = request.Headers.Authorization?.Parameter ?? string.Empty;
            using (var sha = SHA256.Create())
            {
                var tokenHash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
                return (request.RequestUri == null ? string.Empty : request.RequestUri.AbsoluteUri) + "|" + tokenHash;
            }
        }

        private static void CopyContentType(HttpContent content, string mediaType)
        {
            if (!string.IsNullOrWhiteSpace(mediaType))
                content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
    }
}
