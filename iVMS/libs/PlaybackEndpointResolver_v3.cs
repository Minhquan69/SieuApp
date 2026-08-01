using System;
using System.Collections.Generic;
using System.Linq;

namespace V3SClient.libs
{
    internal static class PlaybackEndpointResolver_v3
    {
        internal const string FallbackUrl = "http://192.168.1.199:9999";

        internal static string Resolve(
            IEnumerable<EndpointProfile> endpoints,
            string networkMode,
            out string source)
        {
            EndpointProfile playback = (endpoints ?? Enumerable.Empty<EndpointProfile>())
                .FirstOrDefault(endpoint =>
                    string.Equals(endpoint?.Keyword, "_playback", StringComparison.OrdinalIgnoreCase));

            string modeUrl = string.Equals(networkMode, "Public", StringComparison.OrdinalIgnoreCase)
                ? playback?.PublicUrl
                : playback?.InternalUrl;

            if (TryNormalize(modeUrl, out string resolved))
            {
                source = "mode";
                return resolved;
            }

            if (TryNormalize(playback?.InternalUrl, out resolved))
            {
                source = "internal";
                return resolved;
            }

            source = "fallback";
            return FallbackUrl;
        }

        private static bool TryNormalize(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;

            normalized = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }
    }
}
