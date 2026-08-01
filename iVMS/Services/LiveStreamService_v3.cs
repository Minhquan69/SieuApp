using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.viewModels;

namespace V3SClient.Services
{
    /// <summary>Uses the same WHEP stream-broker contract as the web Live View.</summary>
    public sealed class LiveStreamService_v3
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public async Task<LiveStreamConnection_v3> ConnectAsync(Camera camera, CancellationToken cancellationToken)
        {
            return await ConnectAsync(camera, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<LiveStreamConnection_v3> ConnectAsync(Camera camera, CameraStreamInfo selectedStream, CancellationToken cancellationToken)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var mainStream = selectedStream ?? (camera.Streams == null ? null :
                camera.Streams.FirstOrDefault(stream => string.Equals(stream.StreamType, "main", StringComparison.OrdinalIgnoreCase)) ??
                camera.Streams.FirstOrDefault());
            var cameraId = mainStream != null && !string.IsNullOrWhiteSpace(mainStream.RtspRelayRaw)
                ? mainStream.RtspRelayRaw : camera.camID;
            if (string.IsNullOrWhiteSpace(cameraId)) throw new InvalidOperationException("Camera stream identifier is unavailable.");

            var payload = new
            {
                camera_id = cameraId,
                codec = mainStream == null ? null : mainStream.Codec,
                viewmode = camera.type,
                serverrelay_publicendpoint = camera.ServerRelayPublicEndpoint,
                serverrelay_rtc_endpoint = camera.ServerRelayRtcInternalEndpoint ?? camera.ServerRelayRtcPublicEndpoint,
                serverrelay_rtc_fallback_endpoint = camera.ServerRelayRtcInternalEndpoint == null
                    ? null : camera.ServerRelayRtcPublicEndpoint
            };
            var streamApiUrl = ApiManager.Instance.StreamApiUrl;
            if (string.IsNullOrWhiteSpace(streamApiUrl))
                throw new InvalidOperationException("The Live View stream API URL is not configured.");
            var endpoint = new Uri(streamApiUrl.TrimEnd('/') + "/connect", UriKind.Absolute);
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                // Unlike the browser client, this standalone HttpClient does not inherit
                // the authenticated session automatically. Reuse the token established by
                // the existing login infrastructure without changing the API contract.
                var token = ApiManager.Instance.BackendToken;
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        "Live stream service returned " + (int)response.StatusCode +
                        " from " + endpoint.GetLeftPart(UriPartial.Authority) + endpoint.AbsolutePath + ".");
                var result = JsonConvert.DeserializeObject<LiveStreamConnection_v3>(body);
                if (result == null || string.IsNullOrWhiteSpace(result.PlaybackUrl))
                    throw new InvalidOperationException("Live stream service did not return a playback URL.");
                result.PlaybackUrl = ToAbsoluteUrl(endpoint, result.PlaybackUrl);
                return result;
                }
            }
        }

        public Task DisconnectAsync(IEnumerable<string> cameraIds, CancellationToken cancellationToken)
        {
            return PostCameraIdsAsync("/disconnect/bulk", cameraIds, cancellationToken);
        }

        public async Task<bool> ConnectBulkAsync(IEnumerable<LiveSlotViewModel_v3> slots, CancellationToken cancellationToken)
        {
            var items = (slots ?? Enumerable.Empty<LiveSlotViewModel_v3>())
                .Where(slot => slot != null && slot.Camera != null)
                .Select(slot =>
                {
                    var stream = slot.SelectedStream;
                    return new
                    {
                        camera_id = stream != null && !string.IsNullOrWhiteSpace(stream.RtspRelayRaw) ? stream.RtspRelayRaw : slot.Camera.camID,
                        codec = stream == null ? null : stream.Codec,
                        viewmode = slot.Camera.type,
                        serverrelay_publicendpoint = slot.Camera.ServerRelayPublicEndpoint,
                        serverrelay_rtc_endpoint = slot.Camera.ServerRelayRtcInternalEndpoint ?? slot.Camera.ServerRelayRtcPublicEndpoint,
                        serverrelay_rtc_fallback_endpoint = slot.Camera.ServerRelayRtcInternalEndpoint == null ? null : slot.Camera.ServerRelayRtcPublicEndpoint
                    };
                }).Where(item => !string.IsNullOrWhiteSpace(item.camera_id)).ToArray();
            if (items.Length == 0) return true;
            var streamApiUrl = ApiManager.Instance.StreamApiUrl;
            if (string.IsNullOrWhiteSpace(streamApiUrl)) return false;
            var endpoint = new Uri(streamApiUrl.TrimEnd('/') + "/connect/bulk", UriKind.Absolute);
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(new { items }), Encoding.UTF8, "application/json");
                var token = ApiManager.Instance.BackendToken;
                if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    return response.IsSuccessStatusCode;
            }
        }

        public Task RemoveAsync(IEnumerable<string> cameraIds, CancellationToken cancellationToken)
        {
            return PostCameraIdsAsync("/remove/bulk", cameraIds, cancellationToken);
        }

        public async Task<IList<RoiConfig_v3>> FetchRoisAsync(string cameraId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(cameraId)) return new List<RoiConfig_v3>();
            var streamApiUrl = ApiManager.Instance.StreamApiUrl;
            if (string.IsNullOrWhiteSpace(streamApiUrl)) return new List<RoiConfig_v3>();
            var endpoint = new Uri(streamApiUrl.TrimEnd('/') + "/rois/" + Uri.EscapeDataString(cameraId), UriKind.Absolute);
            using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
            {
                var token = ApiManager.Instance.BackendToken;
                if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return new List<RoiConfig_v3>();
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<List<RoiConfig_v3>>(body) ?? new List<RoiConfig_v3>();
                }
            }
        }

        private static async Task PostCameraIdsAsync(string path, IEnumerable<string> cameraIds, CancellationToken cancellationToken)
        {
            var ids = (cameraIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            if (ids.Length == 0) return;
            var streamApiUrl = ApiManager.Instance.StreamApiUrl;
            if (string.IsNullOrWhiteSpace(streamApiUrl)) throw new InvalidOperationException("The Live View stream API URL is not configured.");
            var endpoint = new Uri(streamApiUrl.TrimEnd('/') + path, UriKind.Absolute);
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(new { camera_ids = ids }), Encoding.UTF8, "application/json");
                var token = ApiManager.Instance.BackendToken;
                if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Live stream service returned " + (int)response.StatusCode + " from " + endpoint.AbsolutePath + ".");
                }
            }
        }

        private static string ToAbsoluteUrl(Uri serviceEndpoint, string url)
        {
            Uri absolute;
            return Uri.TryCreate(url, UriKind.Absolute, out absolute)
                ? absolute.AbsoluteUri
                : new Uri(serviceEndpoint, "/" + url.TrimStart('/')).AbsoluteUri;
        }
    }

    public sealed class LiveStreamConnection_v3
    {
        [JsonProperty("playbackUrl")] public string PlaybackUrl { get; set; }
        [JsonProperty("protocol")] public string Protocol { get; set; }
        [JsonProperty("playerType")] public string PlayerType { get; set; }
    }

    public sealed class RoiConfig_v3
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("points")] public List<double[]> Points { get; set; }
    }
}
