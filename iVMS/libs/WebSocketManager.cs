using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Diagnostics;

namespace V3SClient.libs
{
    public class WebSocketManager : IDisposable
    {
        private static readonly Lazy<WebSocketManager> _instance = new Lazy<WebSocketManager>(() => new WebSocketManager());
        public static WebSocketManager Instance => _instance.Value;

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private string _wsUrl;
        private bool _isConnected = false;
        public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;

        public event Action<string, string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        private WebSocketManager()
        {
        }

        public async Task ConnectAsync(string baseUrl, string token)
        {
            if (_isConnected) await DisconnectAsync();

            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // Convert http(s) to ws(s)
            string wsScheme = baseUrl.StartsWith("https") ? "wss" : "ws";
            string host = baseUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
            
            // Nếu host đã chứa api/v1 ở cuối, ta loại bỏ nó để tránh lặp lại hoặc chuẩn hóa
            if (host.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            {
                host = host.Substring(0, host.Length - 7).TrimEnd('/');
            }

            // Xây dựng URL cuối cùng (Thử đường dẫn chuẩn của CenterManager trước)
            _wsUrl = $"{wsScheme}://{host}/api/v1/ws/events?token={token}";

            try
            {
                LoggerManager.LogInfo($"Đang kết nối WebSocket tới: {_wsUrl}");
                await _webSocket.ConnectAsync(new Uri(_wsUrl), _cts.Token);
                _isConnected = true;
                LoggerManager.LogInfo("Kết nối WebSocket thành công.");
                OnConnected?.Invoke();
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                // Nếu lỗi 404 hoặc không tìm thấy đường dẫn /api/v1/ws, thử fallback về /ws/events (cho bản Postgres cũ)
                if (ex.Message.Contains("404") || (ex.InnerException != null && ex.InnerException.Message.Contains("404")))
                {
                    LoggerManager.LogWarn("Đường dẫn /api/v1/ws/events không tồn tại, đang thử fallback về /ws/events...");
                    string fallbackUrl = $"{wsScheme}://{host}/ws/events?token={token}";
                    try
                    {
                        _webSocket = new ClientWebSocket(); // Tạo mới vì cái cũ đã hỏng state
                        await _webSocket.ConnectAsync(new Uri(fallbackUrl), _cts.Token);
                        _wsUrl = fallbackUrl;
                        _isConnected = true;
                        LoggerManager.LogInfo("Kết nối WebSocket thành công (Fallback).");
                        OnConnected?.Invoke();
                        _ = ReceiveLoop();
                        return;
                    }
                    catch (Exception fallbackEx)
                    {
                        LoggerManager.LogException(fallbackEx, "Kết nối WebSocket Fallback cũng thất bại");
                    }
                }
                
                LoggerManager.LogException(ex, "Lỗi kết nối WebSocket");
                _isConnected = false;
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024 * 4];
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Debug.WriteLine($"WS msg:{message}");
                    LoggerManager.LogDebug($"Nhận WebSocket: {message}");
                    HandleWSMessage(message);
                }
                catch (Exception ex)
                {
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        LoggerManager.LogException(ex, "Lỗi khi nhận message từ WebSocket");
                        await Task.Delay(5000); // Wait and retry/reconnect logic could be added here
                    }
                    break;
                }
            }
        }

        private void HandleWSMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                // Validate if it's a JSON object/array
                string trimmed = json.TrimStart();
                if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
                {
                    Debug.WriteLine($"Ignored non-JSON WS message: {json}");
                    return;
                }

                // The backend sends events as structured JSON
                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                dynamic eventData = JsonConvert.DeserializeObject(json, settings);
                
                // Try to determine the type either by 'type' field or 'CommandType' enum
                string type = eventData.type;
                int? commandType = eventData.CommandType;

                if (type == "TalkStatusEvent" || commandType == (int)CommandType.Talk)
                {
                    OnMessageReceived?.Invoke("Talk", json);
                }
                else if (type == "DeviceStatusEvent" || commandType == (int)CommandType.Notify)
                {
                    OnMessageReceived?.Invoke("DeviceStatus", json);
                }
                else
                {
                    // Fallback: try to see if it has Status and DeviceID
                    if (eventData.DeviceID != null && eventData.Status != null)
                    {
                         OnMessageReceived?.Invoke("DeviceStatus", json);
                    }
                    else
                    {
                        LoggerManager.LogDebug($"Nhận message WebSocket không xác định: {json}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi xử lý message WebSocket");
            }
        }

        public void Subscribe(string channel, Action<string, string> handler)
        {
            // For backward compatibility with RedisPubSubManager interface style
            OnMessageReceived += (ch, msg) =>
            {
                if (ch == channel) handler(ch, msg);
            };
        }

        public async Task DisconnectAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                _webSocket.Dispose();
                _webSocket = null;
            }
             _isConnected = false;
             LoggerManager.LogInfo("Đã đóng kết nối WebSocket.");
             OnDisconnected?.Invoke();
         }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}
