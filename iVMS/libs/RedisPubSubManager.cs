using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace V3SClient.libs
{
    public class RedisPubSubManager
    {
        private static readonly Lazy<RedisPubSubManager> _instance =
        new Lazy<RedisPubSubManager>(() => new RedisPubSubManager());

        private ConnectionMultiplexer _redis;
        private ISubscriber _subscriber;
        private string _redisConnectionString = "localhost:6379";
        private readonly Dictionary<string, Action<RedisChannel, RedisValue>> _subscriptions;
        private readonly object _lock = new object();

        private RedisPubSubManager()
        {
            _subscriptions = new Dictionary<string, Action<RedisChannel, RedisValue>>();

        }

        public static RedisPubSubManager Instance => _instance.Value;

        public void Configure(string hostName, int port = 6379)
        {
            _redisConnectionString = $"{hostName}:{port}";
            Connect();
        }
        private void Connect()
        {
            try
            {
                lock (_lock)
                {
                    _redis = ConnectionMultiplexer.Connect(_redisConnectionString);
                    _subscriber = _redis.GetSubscriber();

                    // Đăng ký lại các channel nếu có ngắt kết nối
                    foreach (var sub in _subscriptions)
                    {
                        _subscriber.Subscribe(sub.Key, sub.Value);
                    }
                    LoggerManager.LogInfo("Kết nối thành công tới Redis và đã đăng ký lại các kênh (channels).");
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Chưa thể kết nối tới Redis, đang kích hoạt quá trình kết nối lại...", ex);
                RetryConnect();
            }
        }

        private void RetryConnect()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (_redis == null || !_redis.IsConnected)
                {
                    try
                    {
                        LoggerManager.LogWarn("Đang cố gắng kết nối lại tới Redis...");
                        Connect();
                        return;
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, "Lỗi xảy ra trong quá trình kết nối lại Redis");
                        Thread.Sleep(5000); // Chờ 5 giây trước khi thử lại
                    }
                }
            });
        }

        public void Publish(string channel, string message)
        {
            try
            {
                _subscriber?.Publish(channel, message);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi Publish message tới channel '{channel}'");
            }
        }
        public void CmdControl(CommandType cmTyp, ControlType control, string deviceID, int serverID = -1)
        {
            string message = "";
            switch (cmTyp)
            {
                case CommandType.Talk:
                    break;

                case CommandType.Control:

                    {
                        ControlCmd controlCmd = new ControlCmd()
                        {
                            ControlType = control,
                            DeviceID = deviceID,
                            ServerID = Guid.NewGuid(),
                            CommandType = CommandType.Control

                        };
                        message = $"";
                        Publish(CommandType.Control.ToString(), controlCmd.ToString());
                    }
                    break;
                case CommandType.Message:
                case CommandType.Notify:
                case CommandType.Group:
                    break;
            }
        }
        public void Subscribe(string channel, Action<RedisChannel, RedisValue> callback)
        {
            try
            {
                lock (_lock)
                {
                    if (!_subscriptions.ContainsKey(channel))
                    {
                        _subscriptions[channel] = callback;
                        _subscriber?.Subscribe(channel, callback);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi Subscribe channel '{channel}'");
            }
        }

        public async void SetMaster(string masterID, int groupID)
        {
            //Cập nhật DB
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            //_=await DatabaseManagerAsync.Instance.UpdateGroupDevices(groupID, masterID, cts.Token);
            //Đồng bộ server
            CmdControl(CommandType.Control, ControlType.RemoveAndReconnectDevice, masterID);
        }
        public void Unsubscribe(string channel)
        {
            try
            {
                lock (_lock)
                {
                    if (_subscriptions.ContainsKey(channel))
                    {
                        _subscriber.Unsubscribe(channel);
                        _subscriptions.Remove(channel);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi Unsubscribe channel '{channel}'");
            }
        }
    }
}
