using System;

namespace V3SClient.libs
{
    public class ClientConfig
    {
        public string ApiUrl { get; set; } = "http://localhost:8100";
        public string StreamApiUrl { get; set; } = "http://localhost:3000/streams";
        public string NetworkMode { get; set; } = "Public"; // "Public" or "Internal"
    }
}















