using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.models
{
    public class CamInfoModel 
    {
        public int Index { get; set; }

        // Camera info from API
        public string Id { get; set; }
        public string CameraCode { get; set; }
        public string DisplayName1 { get; set; }
        public string DisplayName2 { get; set; }
        public string CameraType { get; set; }
        public string Codec { get; set; }
        public string OperationMode { get; set; }
        public string SourceIp { get; set; }
        public int? SourcePort { get; set; }
        public string SourceStreamUrl { get; set; }
        public string LocationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string MediaServerId { get; set; }
        public string MediaServerName { get; set; }
        public List<string> GroupIds { get; set; }
        public string AiNodeName { get; set; }

        // Display helpers
        public string CameraTypeDisplay => CameraType == "ip_cam" ? "IP Camera" : CameraType == "body_cam" ? "Body Camera" : CameraType;
        public string OperationModeDisplay => OperationMode == "ai_processed" ? "Xử lý AI" : "Dữ liệu thô";

        // Legacy compat properties - mapped from CamInfo
        public string CamInfo_CamId => Id;
        public string CamInfo_Name => DisplayName1;
        public string CamInfo_Type => CameraType;
        public string CamInfo_Codec => Codec;
        public string CamInfo_LocationName => LocationName;
    }
}

















