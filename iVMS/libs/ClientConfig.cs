using System;

namespace V3SClient.libs
{
    public class ClientConfig
    {
        public string ApiUrl { get; set; }
        public string StreamApiUrl { get; set; }
        public string StorageUrl { get; set; }
        public string MapUrl { get; set; }
        // Optional absolute WebSocket endpoint for live AI metadata.  When it
        // is empty the desktop client resolves the service from endpoint
        // discovery instead of assuming the web development proxy is running.
        public string MetadataWsUrl { get; set; }
        // URLs and paths for optional live-monitoring integrations. Keep
        // deployment addresses out of the executable so each installation can
        // point at its own AI services without recompiling.
        public string FrameDetectionCountsApiUrl { get; set; }
        public string LiveFrameDetectionCountsApiUrl { get; set; }
        public string CameraVehicleCountsApiUrl { get; set; }
        public string CameraHealthTimeseriesApiUrl { get; set; }
        public string AiEventFeedPath { get; set; }
        public string StorageAccessUrlsApiUrl { get; set; }
        public string StorageAccessUrlsApiToken { get; set; }
        public string NetworkMode { get; set; } = "Public"; // "Public" or "Internal"
    }
}















