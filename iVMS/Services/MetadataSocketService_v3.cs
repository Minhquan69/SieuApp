using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using V3SClient.libs;

namespace V3SClient.Services
{
    public sealed class AiMetadataBox_v3
    {
        public string Label { get; set; }
        public double Confidence { get; set; }
        public bool IsBlacklist { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsInsideRoi { get; set; }
        public string RoiId { get; set; }
        public string RoiDwellSecondsInfo { get; set; }

        // The metadata API can provide both normalized bbox coordinates and
        // source-frame pixels.  Keep both so the native D3D overlay can map
        // the boxes to the decoded texture without relying on WPF layout.
        public bool HasPixelBounds { get; set; }
        public double PixelLeft { get; set; }
        public double PixelTop { get; set; }
        public double PixelWidth { get; set; }
        public double PixelHeight { get; set; }
    }

    public sealed class AiMetadataFrame_v3
    {
        public string CameraId { get; set; }
        public IList<AiMetadataBox_v3> Objects { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
    }

    public sealed class MetadataSocketService_v3 : IDisposable
    {
        public static readonly MetadataSocketService_v3 Instance = new MetadataSocketService_v3();
        private readonly object _sync = new object();
        private readonly Dictionary<string, List<Action<AiMetadataFrame_v3>>> _subscribers = new Dictionary<string, List<Action<AiMetadataFrame_v3>>>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _connectionGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Task _receiveTask;

        private MetadataSocketService_v3() { }

        public IDisposable Subscribe(string cameraId, Action<AiMetadataFrame_v3> callback)
        {
            if (string.IsNullOrWhiteSpace(cameraId) || callback == null) return new Subscription(null);
            lock (_sync)
            {
                List<Action<AiMetadataFrame_v3>> callbacks;
                if (!_subscribers.TryGetValue(cameraId, out callbacks)) _subscribers[cameraId] = callbacks = new List<Action<AiMetadataFrame_v3>>();
                callbacks.Add(callback);
            }
            _ = EnsureConnectedAsync();
            _ = SendSubscriptionsAsync();
            return new Subscription(() => Unsubscribe(cameraId, callback));
        }

        private async Task EnsureConnectedAsync()
        {
            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_socket != null && (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.Connecting)) return;
                _socket?.Dispose();
                _socket = null;
                var connection = await OpenMetadataSocketAsync(_lifetime.Token).ConfigureAwait(false);
                if (connection == null) return;
                _socket = connection.Socket;
                LoggerManager.LogInfo("Live View _v3 metadata WebSocket connected: " + connection.Uri);
                await SendSubscriptionsAsync().ConfigureAwait(false);
                _receiveTask = ReceiveLoopAsync(_socket, _lifetime.Token);
                _ = HeartbeatLoopAsync(_socket, _lifetime.Token);
            }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata WebSocket connection failed"); }
            finally { _connectionGate.Release(); }
        }

        /// <summary>
        /// Resolves the FastAPI metadata hub independently from the browser's
        /// /streams development proxy.  The previous code always used
        /// localhost:3000, which only exists while the Next.js dev server is
        /// running and is why native Live View never received AI messages.
        /// </summary>
        private sealed class MetadataSocketConnection
        {
            public ClientWebSocket Socket { get; set; }
            public Uri Uri { get; set; }
        }

        private static async Task<MetadataSocketConnection> OpenMetadataSocketAsync(CancellationToken token)
        {
            Exception lastError = null;
            foreach (var endpoint in GetMetadataEndpoints())
            {
                var socket = new ClientWebSocket();
                try
                {
                    await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
                    return new MetadataSocketConnection { Socket = socket, Uri = endpoint };
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LoggerManager.LogWarn("Live View _v3 metadata endpoint unavailable: " + endpoint + " (" + ex.Message + ")");
                    try { socket.Abort(); } catch { }
                    socket.Dispose();
                }
            }

            if (lastError != null) throw new InvalidOperationException(
                "Không thể kết nối dịch vụ AI metadata. Cấu hình MetadataWsUrl tới FastAPI /ws/metadata.", lastError);
            return null;
        }

        private static IEnumerable<Uri> GetMetadataEndpoints()
        {
            var values = new List<string>();
            var configured = ConfigurationManager.AppSettings["MetadataWsUrl_v3"];
            if (!string.IsNullOrWhiteSpace(configured)) values.Add(configured);
            if (!string.IsNullOrWhiteSpace(ApiManager.Instance.MetadataWsUrl)) values.Add(ApiManager.Instance.MetadataWsUrl);

            // The metadata FastAPI app is deployed with the Assets service in
            // the current system.  Its discovered URL may end with /static.
            var assets = ApiManager.Instance.GetEndpointUrl("Assets");
            if (!string.IsNullOrWhiteSpace(assets)) values.Add(assets);
            var stream = ApiManager.Instance.StreamApiUrl;
            if (!string.IsNullOrWhiteSpace(stream) &&
                stream.IndexOf("localhost:3000", StringComparison.OrdinalIgnoreCase) < 0)
                values.Add(stream);
            if (!string.IsNullOrWhiteSpace(ApiManager.Instance.BaseUrl)) values.Add(ApiManager.Instance.BaseUrl);

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                Uri source;
                if (!Uri.TryCreate(value, UriKind.Absolute, out source)) continue;
                var path = source.AbsolutePath ?? string.Empty;
                var isExplicitMetadataPath = path.EndsWith("/ws/metadata", StringComparison.OrdinalIgnoreCase);
                if (!isExplicitMetadataPath && path.EndsWith("/static", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - "/static".Length);
                if (!isExplicitMetadataPath && path.EndsWith("/streams", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - "/streams".Length);
                var uri = new UriBuilder(source)
                {
                    Scheme = string.Equals(source.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
                    Path = isExplicitMetadataPath ? path : path.TrimEnd('/') + "/ws/metadata",
                    Query = string.Empty
                }.Uri;
                if (emitted.Add(uri.AbsoluteUri)) yield return uri;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var builder = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    Dispatch(builder.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata WebSocket receive failed"); }
            finally
            {
                if (!token.IsCancellationRequested && HasSubscribers())
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    await EnsureConnectedAsync().ConfigureAwait(false);
                }
            }
        }

        private void Dispatch(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                if (!string.Equals((string)root["type"], "ai_metadata", StringComparison.OrdinalIgnoreCase)) return;
                var cameraId = (string)root["camera_id"];
                var debug = root["debug"] as JObject;
                var frame = new AiMetadataFrame_v3
                {
                    CameraId = cameraId,
                    Objects = new List<AiMetadataBox_v3>(),
                    SourceWidth = (int?)debug?["source_width"] ?? 0,
                    SourceHeight = (int?)debug?["source_height"] ?? 0,
                    ReceivedAtUtc = DateTime.UtcNow
                };
                foreach (var item in root["objects"] as JArray ?? new JArray())
                {
                    // Match WebApp isNormalAiObject: normal detections are
                    // transport/status metadata, not a drawable AI bbox.
                    if (IsNormalAiObject(item)) continue;
                    var bbox = item["bbox"];
                    if (bbox == null) continue;
                    var pixel = item["bbox_pixel"] as JObject;
                    string roiDwellSecondsInfo, roiId;
                    var isInsideRoi = TryGetRoiDwellInfo(item, out roiDwellSecondsInfo, out roiId);
                    frame.Objects.Add(new AiMetadataBox_v3
                    {
                        Label = (string)item["name"] ?? (string)item["label"] ?? "object",
                        Confidence = (double?)item["confidence"] ?? 0,
                        IsBlacklist = (bool?)item["is_blacklist"] ?? false,
                        Left = (double?)bbox["left"] ?? (double?)bbox["x"] ?? 0,
                        Top = (double?)bbox["top"] ?? (double?)bbox["y"] ?? 0,
                        Width = (double?)bbox["width"] ?? 0,
                        Height = (double?)bbox["height"] ?? 0,
                        IsInsideRoi = isInsideRoi,
                        RoiId = roiId,
                        RoiDwellSecondsInfo = roiDwellSecondsInfo,
                        HasPixelBounds = pixel != null,
                        PixelLeft = pixel == null ? 0 : (double?)pixel["left"] ?? (double?)pixel["x"] ?? 0,
                        PixelTop = pixel == null ? 0 : (double?)pixel["top"] ?? (double?)pixel["y"] ?? 0,
                        PixelWidth = pixel == null ? 0 : (double?)pixel["width"] ?? 0,
                        PixelHeight = pixel == null ? 0 : (double?)pixel["height"] ?? 0
                    });
                }
                Action<AiMetadataFrame_v3>[] callbacks;
                lock (_sync)
                {
                    List<Action<AiMetadataFrame_v3>> list;
                    callbacks = _subscribers.TryGetValue(cameraId ?? string.Empty, out list) ? list.ToArray() : new Action<AiMetadataFrame_v3>[0];
                }
                foreach (var callback in callbacks) callback(frame);
            }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata message parse failed"); }
        }

        private static bool IsNormalAiObject(JToken item)
        {
            var objectName = ((string)item["name"] ?? string.Empty).Trim();
            if (string.Equals(objectName, "normal", StringComparison.OrdinalIgnoreCase)) return true;
            var identifiers = item["detected_object_ids"];
            var values = identifiers is JArray
                ? identifiers.Values<string>()
                : new[] { identifiers == null ? null : identifiers.ToString() };
            return values.Any(value => string.Equals((value ?? string.Empty).Trim(), "normal", StringComparison.OrdinalIgnoreCase));
        }

        // Metadata follows the same shape used by WebApp/base RtspPlayer:
        // object_analysis[].roi_inside plus roi_dwell_seconds (or *_ms).
        private static bool TryGetRoiDwellInfo(JToken item, out string dwellInfo, out string roiId)
        {
            dwellInfo = null;
            roiId = null;
            var sources = new List<JObject>();
            var root = item as JObject;
            if (root != null) sources.Add(root);
            var analysis = root == null ? null : root["object_analysis"];
            var analysisItems = analysis as JArray;
            if (analysisItems != null)
                sources.AddRange(analysisItems.OfType<JObject>());
            else if (analysis is JObject)
                sources.Add((JObject)analysis);

            foreach (var source in sources)
            {
                var insideMap = source["roi_inside"] as JObject;
                if (insideMap == null) continue;
                foreach (var roi in insideMap.Properties())
                {
                    if (!IsTruthy(roi.Value)) continue;
                    roiId = roi.Name;
                    var value = GetRoiMapValue(sources, "roi_dwell_seconds", roi.Name);
                    var isMilliseconds = false;
                    if (value == null)
                    {
                        value = GetRoiMapValue(sources, "roi_dwell_ms", roi.Name);
                        isMilliseconds = true;
                    }
                    dwellInfo = FormatRoiDwell(value, isMilliseconds);
                    return true;
                }
            }
            return false;
        }

        private static JToken GetRoiMapValue(IEnumerable<JObject> sources, string key, string roiId)
        {
            foreach (var source in sources)
            {
                var map = source[key] as JObject;
                JToken value;
                if (map != null && map.TryGetValue(roiId, StringComparison.OrdinalIgnoreCase, out value)) return value;
            }
            return null;
        }

        private static bool IsTruthy(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return false;
            if (value.Type == JTokenType.Boolean) return value.Value<bool>();
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return value.Value<double>() != 0;
            var text = value.ToString().Trim();
            return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "y", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatRoiDwell(JToken value, bool isMilliseconds)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            double seconds;
            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                return (isMilliseconds ? seconds / 1000d : seconds).ToString("0.0", CultureInfo.InvariantCulture) + "s";
            return value + "s";
        }

        private async Task SendSubscriptionsAsync()
        {
            ClientWebSocket socket;
            string[] ids;
            lock (_sync) { socket = _socket; ids = _subscribers.Keys.ToArray(); }
            if (socket == null || socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(new JObject { ["action"] = "set_subscriptions", ["camera_ids"] = new JArray(ids) }.ToString(Newtonsoft.Json.Formatting.None));
            try { await SendAsync(socket, bytes, _lifetime.Token).ConfigureAwait(false); }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata subscription update failed"); }
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                    if (socket.State == WebSocketState.Open)
                        await SendAsync(socket, Encoding.UTF8.GetBytes("{\"action\":\"ping\"}"), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata heartbeat failed"); }
        }

        private async Task SendAsync(ClientWebSocket socket, byte[] bytes, CancellationToken token)
        {
            await _sendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        private void Unsubscribe(string cameraId, Action<AiMetadataFrame_v3> callback)
        {
            var idle = false;
            lock (_sync)
            {
                List<Action<AiMetadataFrame_v3>> callbacks;
                if (_subscribers.TryGetValue(cameraId, out callbacks))
                {
                    callbacks.Remove(callback);
                    if (callbacks.Count == 0) _subscribers.Remove(cameraId);
                }
                idle = _subscribers.Count == 0;
            }
            if (idle)
            {
                var socket = _socket;
                _socket = null;
                if (socket != null) { try { socket.Abort(); } catch { } socket.Dispose(); }
            }
            else _ = SendSubscriptionsAsync();
        }

        private bool HasSubscribers() { lock (_sync) return _subscribers.Count > 0; }
        public void Dispose() { _lifetime.Cancel(); _socket?.Dispose(); _connectionGate.Dispose(); _sendGate.Dispose(); _lifetime.Dispose(); }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
        }
    }
}
