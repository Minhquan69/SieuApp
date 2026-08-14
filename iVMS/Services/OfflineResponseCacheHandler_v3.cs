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

        public OfflineResponseCacheHandler_v3(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public static void Clear()
        {
            Cache.Clear();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null || request.Method != HttpMethod.Get)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var key = BuildCacheKey(request);
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    Cache[key] = new CachedResponse
                    {
                        StoredAtUtc = DateTime.UtcNow,
                        StatusCode = response.StatusCode,
                        Body = body,
                        MediaType = response.Content.Headers.ContentType?.MediaType
                    };
                    response.Content.Dispose();
                    response.Content = new ByteArrayContent(body);
                    CopyContentType(response.Content, Cache[key].MediaType);
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

        private static bool TryCreateFallback(string key, HttpRequestMessage request, out HttpResponseMessage fallback)
        {
            CachedResponse cached;
            if (!Cache.TryGetValue(key, out cached) ||
                DateTime.UtcNow - cached.StoredAtUtc > MaximumStaleAge)
            {
                fallback = null;
                return false;
            }

            fallback = new HttpResponseMessage(cached.StatusCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(cached.Body ?? new byte[0])
            };
            CopyContentType(fallback.Content, cached.MediaType);
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
