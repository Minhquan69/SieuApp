using System;

namespace V3SClient.libs
{
    public class ClientConfig
    {
        public string ApiUrl { get; set; }
        public string StreamApiUrl { get; set; }
        public string StorageUrl { get; set; }
        public string MapUrl { get; set; }
        // Service keys resolved from https://portal.ivistatech.vn/api/system/endpoints.
        // The client always tries public_url first and internal_url only when
        // the public endpoint cannot serve the request.
        public string StorageEndpointKeyword { get; set; } = "Storage";
        public string AssetsEndpointKeyword { get; set; } = "Assets";
        public string ReportEndpointKeyword { get; set; } = "Report";
        public string MapEndpointKeyword { get; set; } = "Map";
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
        public string AiReportEndpointKeyword { get; set; }
        public string RoiConfigEndpointKeyword { get; set; }
        public string RoiConfigApiUrl { get; set; }
        public string RoiConfigApiToken { get; set; }
        public double RoiThresholdSeconds { get; set; } = 5;
        public double RoiMergeGapSeconds { get; set; } = 200;
        public string CameraHealthTimeseriesApiUrl { get; set; }
        public string VehicleStatsApiToken { get; set; }
        public string AiEventFeedPath { get; set; }
        public string AiEventSummaryPath { get; set; }
        public string AiEventRoiObjectsPath { get; set; }
        public string StorageAccessUrlsApiUrl { get; set; }
        public string StorageAccessUrlsApiToken { get; set; }
        // Keyword resolved from Portal endpoint discovery. The corresponding
        // public_url is attempted before internal_url.
        public string DeviceReportEndpointKeyword { get; set; } = "_deviceReport";
        public string NetworkMode { get; set; } = "Public"; // "Public" or "Internal"
        // Optional dedicated device-status service URL (e.g. https://devicestatus.ivistatech.vn).
        // When absent the main ApiUrl is used as the endpoint root.
        public string DeviceStatusApiUrl { get; set; }
        public string DeviceStatusApiKey { get; set; }
        // Fallback registry used when the Portal endpoint discovery is unavailable.
        public string EndpointRegistryUrl { get; set; }
        public string EndpointRegistryApiKey { get; set; }
    }
}
