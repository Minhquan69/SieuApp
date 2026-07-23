using System;

namespace V3SClient.libs
{
    public class ClientConfig
    {
        public string ApiUrl { get; set; } = "http://localhost:8100";
        public string StreamApiUrl { get; set; } = "http://localhost:3000/streams";
        // Optional absolute WebSocket endpoint for live AI metadata.  When it
        // is empty the desktop client resolves the service from endpoint
        // discovery instead of assuming the web development proxy is running.
        public string MetadataWsUrl { get; set; }
        public string NetworkMode { get; set; } = "Public"; // "Public" or "Internal"
    }
}















