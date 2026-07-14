using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
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
    }

    public sealed class AiMetadataFrame_v3
    {
        public string CameraId { get; set; }
        public IList<AiMetadataBox_v3> Objects { get; set; }
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
                _socket = new ClientWebSocket();
                var streamUrl = ApiManager.Instance.StreamApiUrl;
                if (string.IsNullOrWhiteSpace(streamUrl)) return;
                var source = new Uri(streamUrl, UriKind.Absolute);
                var builder = new UriBuilder(source) { Scheme = source.Scheme == "https" ? "wss" : "ws", Path = "/ws/metadata", Query = string.Empty };
                await _socket.ConnectAsync(builder.Uri, _lifetime.Token).ConfigureAwait(false);
                await SendSubscriptionsAsync().ConfigureAwait(false);
                _receiveTask = ReceiveLoopAsync(_socket, _lifetime.Token);
                _ = HeartbeatLoopAsync(_socket, _lifetime.Token);
            }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 metadata WebSocket connection failed"); }
            finally { _connectionGate.Release(); }
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
                var frame = new AiMetadataFrame_v3 { CameraId = cameraId, Objects = new List<AiMetadataBox_v3>() };
                foreach (var item in root["objects"] as JArray ?? new JArray())
                {
                    var bbox = item["bbox"];
                    if (bbox == null) continue;
                    frame.Objects.Add(new AiMetadataBox_v3
                    {
                        Label = (string)item["label"] ?? (string)item["name"] ?? "object",
                        Confidence = (double?)item["confidence"] ?? 0,
                        IsBlacklist = (bool?)item["is_blacklist"] ?? false,
                        Left = (double?)bbox["left"] ?? (double?)bbox["x"] ?? 0,
                        Top = (double?)bbox["top"] ?? (double?)bbox["y"] ?? 0,
                        Width = (double?)bbox["width"] ?? 0,
                        Height = (double?)bbox["height"] ?? 0
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
