using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace V3SClient.models
{

    public class ObjectLocation
    {
        [JsonProperty("left")]
        public float Left { get; set; }

        [JsonProperty("top")]
        public float Top { get; set; }

        [JsonProperty("width")]
        public float Width { get; set; }

        [JsonProperty("height")]
        public float Height { get; set; }
    }

    public class ObjectAnalysis
    {
        [JsonProperty("roi_inside")]
        public Dictionary<string, bool> RoiInside { get; set; }

        [JsonProperty("roi_dwell_seconds")]
        public Dictionary<string, float> RoiDwellSeconds { get; set; }
    }

    public class AIResult
    {

        [JsonProperty("meta_type")] // Face, Plate, Person, Car
        public string MetaType { get; set; }
        [JsonProperty("tracking_object_id")] // th? t? c?a d?i tu?ng m?i trong tracking
        public string TrackingObjectIndex { get; set; }

        [JsonProperty("bbox")] // bounding box
        public ObjectLocation Bbox { get; set; }

        [JsonProperty("is_blacklist")]
        public bool IsBlacklist { get; set; }

        [JsonProperty("detected_object_ids")] // Ð?nh danh c?a d?i tu?ng Bi?n s? xe, ho?c CCCD/ tên thu m?c ch?a ?nh blacklist
        public string ObjectID { get; set; }
        [JsonProperty("name")] // Tên t?i Ph?m
        public string Caption { get; set; }
        [JsonProperty("object_image")] // ?nh d?i tu?ng dã mã hóa base64
        public string EncodedObjectImage { get; set; }
        [JsonProperty("event_type")] // appear, disappear, update
        public string EventType { get; set; }
        [JsonProperty("time_stamp")] // th?i di?m xu?t hi?n meta
        public string TimeStamp { get; set; }

        [JsonProperty("confidence")] // d? tin c?y c?a k?t qu? nh?n di?n, t? 0 d?n 1
        public float Confidence { get; set; }

        [JsonProperty("object_analysis")]
        public List<ObjectAnalysis> ObjectAnalysisList { get; set; }
    }

    public class GpsData
    {
        [JsonProperty("longitude")]
        public double Longitude { get; set; }

        [JsonProperty("latitude")]
        public double Latitude { get; set; }
    }

    public class ImageInfo
    {
        [JsonProperty("width")]
        public int ImageWidth { get; set; }

        [JsonProperty("height")]
        public int ImageHeight { get; set; }
    }

    public class MetaFrame
    {
        [JsonProperty("ai_results")]
        public List<AIResult> AiResults { get; set; }

        [JsonProperty("gps")]
        public GpsData Gps { get; set; }

        [JsonProperty("image")]
        public ImageInfo ImageInfo { get; set; }

        [JsonProperty("ntp_timestamp")]
        public long NtpTimestamp { get; set; }
    }
}















