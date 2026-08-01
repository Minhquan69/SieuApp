using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace V3SClient.libs
{
  
    public class RegionInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class CameraStreamInfo
    {
        [JsonProperty("stream_id")]
        public string StreamId { get; set; }
        
        [JsonProperty("stream_type")]
        public string StreamType { get; set; }
        
        [JsonProperty("source_url")]
        public string SourceUrl { get; set; }
        
        [JsonProperty("codec")]
        public string Codec { get; set; }
        
        [JsonProperty("resolution")]
        public string Resolution { get; set; }
        
        [JsonProperty("is_ai_mode")]
        public bool? IsAiMode { get; set; }
        
        [JsonProperty("meta_embedding")]
        public bool? MetaEmbedding { get; set; }
        
        [JsonProperty("rtsp_relay_raw")]
        public string RtspRelayRaw { get; set; }
        
        [JsonProperty("rtsp_relay_ai")]
        public string RtspRelayAi { get; set; }
    }


    public class CamInfo: INotifyPropertyChanged
    {
        //Client Info
        [JsonProperty("clientinfo_id")]
        public string ClientInfo_Id { get; set; }
        [JsonProperty("clientinfo_name")]
        public string ClientInfo_Name { get; set; }

        //Camera Infomation
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("caminfo_camid")]
        public string CamInfo_CamId { get; set; }
        [JsonProperty("caminfo_type")]
        public string CamInfo_Type { get; set; }
        [JsonProperty("caminfo_name")]
        public string CamInfo_Name { get; set; }
        [JsonProperty("caminfo_longname")]
        public string CamInfo_LongName { get; set; }
        [JsonProperty("caminfo_location")]
        public string CamInfo_Location { get; set; }
        [JsonProperty("caminfo_locationname")]
        public string CamInfo_LocationName { get; set; }

        [JsonProperty("caminfo_codec")]
        public string CamInfo_Codec { get; set; }
        [JsonProperty("caminfo_viewmode")]
        public string CamInfo_ViewMode { get; set; }
        [JsonProperty("device_role")]
        public string Device_Role { get; set; }
        [JsonProperty("region_path")]
        public List<RegionInfo> Region_Path { get; set; }
        [JsonProperty("talk_group_path")]
        public List<RegionInfo> Talk_Group_Path { get; set; }

        [JsonProperty("serverrelay_internalendpoint")]
        public string ServerRelay_InternalEndpoint { get; set; }
        [JsonProperty("serverrelay_publicendpoint")]
        public string ServerRelay_PublicEndpoint {  get; set; }
        [JsonProperty("serverrelay_endpoints")]
        public Dictionary<string, Dictionary<string, string>> ServerRelay_Endpoints { get; set; }
        
        [JsonProperty("caminfo_source_path")]
        public string CamInfo_Source_Path { get; set; }
        
        [JsonProperty("streams")]
        public List<CameraStreamInfo> Streams { get; set; }

        [JsonProperty("is_recording")]
        public bool is_recording { get; set; }
        
        [JsonIgnore]
        public bool HasAIStream => Streams != null && Streams.Any(s => s.IsAiMode == true);

        [JsonProperty("group_id")]
        public int Group_Id { get; set; }

        [JsonProperty("latitude")]
        public double? Latitude { get; set; }
        [JsonProperty("longitude")]
        public double? Longitude { get; set; }
        [JsonProperty("extra_metadata")]
        public object ExtraMetadata { get; set; }

        [JsonProperty("area_id")]
        public string Area_Id { get; set; }
        [JsonProperty("area_name")]
        public string Area_Name { get; set; }

        [JsonProperty("unit_id")]
        public string Unit_Id { get; set; }
        [JsonProperty("unit_name")]
        public string Unit_Name { get; set; }

       
        //Status online /offline
        private string status="offline";
        public string Status
        {
            get => status;
            set
            {
                if (status != value)
                {
                    status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public override string ToString()
        {
            return CamInfo_Name;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

    public class CamInfoComparer : IEqualityComparer<CamInfo>
    {
        public bool Equals(CamInfo x, CamInfo y)
        {
            if (x == null || y == null) return false;
            return x.CamInfo_CamId == y.CamInfo_CamId;
        }

        public int GetHashCode(CamInfo obj)
        {
            return obj.CamInfo_CamId.GetHashCode();
        }
    }
}















