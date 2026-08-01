using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace V3SClient.libs
{
    public enum CommandType
    {
        Control,
        Notify,
        Group,
        Message,
        Talk
    }
    public class CommandBase
    {
        public Guid ServerID { get; set; }
        public CommandType CommandType { get; set; }

        [JsonProperty("device_id")]
        public string DeviceID { get; set; }
        
        // Support legacy name if needed (Json.NET will try both)
        [JsonProperty("DeviceID")]
        private string _deviceID { set => DeviceID = value; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        public static T FromString<T>(string json) where T : CommandBase
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

    }
    public enum ControlType
    {
        StartService,
        StopService,
        StartMediaStream,
        StopMediaStream,
        StartGpsStream,
        StopGpsStream,
        StartTalkStream,
        StopTalkStream,

        RemoveDeviceOnServer,
        RemoveAndReconnectDevice,

        ServerReloadConfig,//not include
        DeviceChangeGroup,//not include
        SetFeedback

    }
    public class ControlCmd : CommandBase
    {
        public ControlType ControlType { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        public static ControlCmd FromString(string json)
        {
            return JsonConvert.DeserializeObject<ControlCmd>(json);
        }

    }
    public enum TalkStatus
    {
        Off,
        On
    }
    public class TalkStatusEvent : CommandBase
    {
        public TalkStatus Status { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        public static TalkStatusEvent FromString(string json)
        {
            return JsonConvert.DeserializeObject<TalkStatusEvent>(json);
        }
    }
    public enum DeviceStatus
    {
        Offline,
        Online
    }
    public class DeviceStatusEvent : CommandBase
    {
        public DeviceStatus Status { get; set; }

        [JsonProperty("is_online")]
        public bool? IsOnline { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        public static DeviceStatusEvent FromString(string json)
        {
            return JsonConvert.DeserializeObject<DeviceStatusEvent>(json);
        }
    }
}















