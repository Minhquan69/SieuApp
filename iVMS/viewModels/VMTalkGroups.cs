using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using GLib;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.viewModels
{
    public class VMTalkGroups
    {
        public ObservableCollection<VMTalkGroup> CamGroupList { get; set; }
        public VMTalkGroups()
        {
            CamGroupList = new ObservableCollection<VMTalkGroup>();
            List<CamInfo> lsCamInfoActive = GlobalUserInfo.Instance.GetActiveClient();
            if (lsCamInfoActive == null || lsCamInfoActive.Count == 0)
            {
                CamGroupList = new ObservableCollection<VMTalkGroup>();
                return;
            }

            // 1. Convert CamInfo to models.Camera
            var camActives = new List<models.Camera>();
            foreach (var camInfo in lsCamInfoActive)
            {
                var camera = new models.Camera
                {
                    camID = camInfo.CamInfo_CamId,
                    groupID = camInfo.Group_Id.ToString(),
                    name = camInfo.CamInfo_Name,
                    long_Name = string.IsNullOrEmpty(camInfo.CamInfo_LongName) ? camInfo.CamInfo_Name : camInfo.CamInfo_LongName,
                    type = camInfo.CamInfo_Type,
                    is_Live = true,
                    // Preserve the API recording capability for Live and
                    // Playback camera-list filters (REC).
                    is_recording = camInfo.is_recording,
                    is_Master = camInfo.Device_Role != "client_device",
                    is_H264 = camInfo.CamInfo_Codec?.ToLower() == "h264",
                    Latitude = camInfo.Latitude,
                    Longitude = camInfo.Longitude,
                    ExtraMetadata = camInfo.ExtraMetadata
                };

                // Fallback: Nếu Latitude/Longitude null, thử phân giải từ chuỗi CamInfo_Location (định dạng "lat, lng")
                if ((camera.Latitude == null || camera.Latitude == 0) && !string.IsNullOrEmpty(camInfo.CamInfo_Location))
                {
                    try
                    {
                        var parts = camInfo.CamInfo_Location.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat))
                                camera.Latitude = lat;
                            if (double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lng))
                                camera.Longitude = lng;
                        }
                    }
                    catch { }
                }

                // Pass streams to camera
                camera.Streams = camInfo.Streams;
                camera.ServerRelayPublicEndpoint = camInfo.ServerRelay_PublicEndpoint;
                if (camInfo.ServerRelay_Endpoints != null && camInfo.ServerRelay_Endpoints.ContainsKey("rtc"))
                {
                    var rtcEndpoints = camInfo.ServerRelay_Endpoints["rtc"];
                    camera.ServerRelayRtcInternalEndpoint = rtcEndpoints.ContainsKey("internal") ? rtcEndpoints["internal"] : null;
                    camera.ServerRelayRtcPublicEndpoint = rtcEndpoints.ContainsKey("public") ? rtcEndpoints["public"] : null;
                }
                
                // Build Multi-Stream URLs
                string endpoint = null;
                string protocol = "rtsp"; // default
                string relayUsername = "admin";
                string relayPassword = "ivista";

                if (camInfo.ServerRelay_Endpoints != null)
                {
#if USE_STREAM_TLS
                    var protocols = new[] { "rtsps", "rtsp" };
#else
                    var protocols = new[] { "rtsp", "rtsps" };
#endif
                    foreach (var p in protocols)
                    {
                        if (camInfo.ServerRelay_Endpoints.ContainsKey(p))
                        {
                            var epDict = camInfo.ServerRelay_Endpoints[p];
                            // GStreamer runs outside the Docker network. Prefer the
                            // public relay for every network mode, then fall back
                            // to an internal endpoint only when no public relay is
                            // supplied. Container-only MediaMTX hosts are rejected.
                            var publicEndpoint = epDict.ContainsKey("public") ? epDict["public"] : null;
                            var internalEndpoint = epDict.ContainsKey("internal") ? epDict["internal"] : null;
                            endpoint = IsUsableRelayEndpoint(publicEndpoint)
                                ? publicEndpoint
                                : (IsUsableRelayEndpoint(internalEndpoint) ? internalEndpoint : null);

                            if (!string.IsNullOrWhiteSpace(endpoint))
                            {
                                relayUsername = epDict.ContainsKey("username") && !string.IsNullOrWhiteSpace(epDict["username"])
                                    ? epDict["username"] : relayUsername;
                                relayPassword = epDict.ContainsKey("password") && !string.IsNullOrWhiteSpace(epDict["password"])
                                    ? epDict["password"] : relayPassword;
                            }
                            
                            if (!string.IsNullOrEmpty(endpoint))
                            {
                                protocol = p;
                                break; // found a working endpoint
                            }
                        }
                    }
                }

                // If no endpoint found from new structure, fallback to old structure
                if (string.IsNullOrEmpty(endpoint))
                {
                    endpoint = IsUsableRelayEndpoint(camInfo.ServerRelay_PublicEndpoint)
                        ? camInfo.ServerRelay_PublicEndpoint
                        : (IsUsableRelayEndpoint(camInfo.ServerRelay_InternalEndpoint)
                            ? camInfo.ServerRelay_InternalEndpoint : null);
#if USE_STREAM_TLS
                    protocol = "rtsps";
#else
                    protocol = "rtsp";
#endif
                }

                // The migrated desktop client uses GStreamer directly; it
                // must never depend on a Docker/MediaMTX hostname. Prefer a
                // public relay endpoint when the selected internal endpoint
                // is a container name, and otherwise derive the authority
                // from the camera's own RTSP source URL.
                if (!string.IsNullOrEmpty(endpoint) && endpoint.IndexOf("mediamtx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    endpoint = camInfo.ServerRelay_PublicEndpoint;
                    protocol = "rtsp";
                    if (!string.IsNullOrEmpty(endpoint) && endpoint.IndexOf("mediamtx", StringComparison.OrdinalIgnoreCase) >= 0)
                        endpoint = null;
                }
                if (string.IsNullOrEmpty(endpoint) && Uri.TryCreate(camInfo.CamInfo_Source_Path, UriKind.Absolute, out var sourceUri) &&
                    (sourceUri.Scheme == "rtsp" || sourceUri.Scheme == "rtsps"))
                {
                    endpoint = sourceUri.Host + (sourceUri.IsDefaultPort ? string.Empty : ":" + sourceUri.Port);
                    protocol = sourceUri.Scheme;
                    if (!string.IsNullOrWhiteSpace(sourceUri.UserInfo))
                    {
                        var userInfo = sourceUri.UserInfo.Split(new[] { ':' }, 2);
                        relayUsername = Uri.UnescapeDataString(userInfo[0]);
                        if (userInfo.Length > 1) relayPassword = Uri.UnescapeDataString(userInfo[1]);
                    }
                }
                if (string.IsNullOrEmpty(endpoint))
                    LoggerManager.LogWarn("Camera " + camInfo.CamInfo_CamId +
                        " has no usable direct RTSP endpoint. public=" + (camInfo.ServerRelay_PublicEndpoint ?? "<none>") +
                        ", internal=" + (camInfo.ServerRelay_InternalEndpoint ?? "<none>") +
                        ", source=" + (camInfo.CamInfo_Source_Path ?? "<none>") +
                        ". MediaMTX/Docker fallback was not used.");

                string baseUrl = string.IsNullOrEmpty(endpoint)
                    ? null
                    : $"{protocol}://{Uri.EscapeDataString(relayUsername)}:{Uri.EscapeDataString(relayPassword)}@{endpoint}";

                if (camInfo.Streams != null && camInfo.Streams.Count > 0)
                {
                    // Find main and sub streams
                    var mainStream = camInfo.Streams.FirstOrDefault(s => s.StreamType?.ToLower() == "main");
                    var subStreams = camInfo.Streams.Where(s => s.StreamType?.ToLower().StartsWith("sub") == true)
                                                    .OrderBy(s => s.StreamType) // sub1, sub2, etc.
                                                    .ToList();

                    var bestSubStreamRaw = subStreams.FirstOrDefault();
                    var bestSubStreamAI = subStreams.FirstOrDefault(s => s.IsAiMode == true);
                    
                    var mainStreamRaw = mainStream;
                    var mainStreamAI = mainStream?.IsAiMode == true ? mainStream : null;

                    bool isCodecH264(string codec, bool defaultVal)
                    {
                        if (string.IsNullOrEmpty(codec) || codec.Equals("unknown", StringComparison.OrdinalIgnoreCase)) 
                            return defaultVal;
                        var lower = codec.ToLower();
                        return lower.Contains("264") || lower.Contains("avc");
                    }

                    // URL RAW
                    var targetRaw = bestSubStreamRaw ?? mainStreamRaw ?? camInfo.Streams.FirstOrDefault();
                    camera.RtspUrlRaw = !string.IsNullOrEmpty(endpoint) && targetRaw != null && !string.IsNullOrEmpty(targetRaw.RtspRelayRaw) ? $"{baseUrl}{targetRaw.RtspRelayRaw}" : null;
                    camera.IsH264Raw = isCodecH264(targetRaw?.Codec, camera.is_H264);
                    
                    var targetMainRaw = mainStreamRaw ?? targetRaw;
                    camera.RtspUrlMainRaw = !string.IsNullOrEmpty(endpoint) && targetMainRaw != null && !string.IsNullOrEmpty(targetMainRaw.RtspRelayRaw) ? $"{baseUrl}{targetMainRaw.RtspRelayRaw}" : null;
                    camera.IsH264MainRaw = isCodecH264(targetMainRaw?.Codec, camera.is_H264);

                    // URL AI
                    var targetAI = bestSubStreamAI ?? mainStreamAI;
                    string aiPath = targetAI?.RtspRelayAi;
                    if (string.IsNullOrEmpty(aiPath)) aiPath = targetAI?.RtspRelayRaw;
                    
                    camera.RtspUrlAI = !string.IsNullOrEmpty(endpoint) && targetAI != null && !string.IsNullOrEmpty(aiPath) ? $"{baseUrl}{aiPath}" : null;
                    camera.IsH264AI = isCodecH264(targetAI?.Codec, camera.is_H264);
                    
                    var targetMainAI = mainStreamAI ?? targetAI;
                    string mainAiPath = targetMainAI?.RtspRelayAi;
                    if (string.IsNullOrEmpty(mainAiPath)) mainAiPath = targetMainAI?.RtspRelayRaw;

                    camera.RtspUrlMainAI = !string.IsNullOrEmpty(endpoint) && targetMainAI != null && !string.IsNullOrEmpty(mainAiPath) ? $"{baseUrl}{mainAiPath}" : null;
                    camera.IsH264MainAI = isCodecH264(targetMainAI?.Codec, camera.is_H264);

                    camera.HasAIStream = camInfo.Streams.Any(s => s.IsAiMode == true);
                }
                else
                {
                    // Fallback to legacy logic if no streams available
                    if (camInfo.CamInfo_ViewMode == "raw_data" && IsDirectRtspUrl(camInfo.CamInfo_Source_Path))
                    {
#if USE_STREAM_TLS
                        camera.RtspUrlRaw = camInfo.CamInfo_Source_Path?.Replace("rtsp://", "rtsps://");
                        camera.RtspUrlMainRaw = camInfo.CamInfo_Source_Path?.Replace("rtsp://", "rtsps://");
#else
                        camera.RtspUrlRaw = camInfo.CamInfo_Source_Path;
                        camera.RtspUrlMainRaw = camInfo.CamInfo_Source_Path;
#endif
                    }
                    else
                    {
                        //fallback 
                        camera.RtspUrlRaw = string.IsNullOrEmpty(endpoint) ? null : $"{baseUrl}/live/{camInfo.CamInfo_CamId}/main";
                        camera.RtspUrlMainRaw = camera.RtspUrlRaw;
                    }
                }

                // Default backward compatibility
                var directFallback = IsDirectRtspUrl(camInfo.CamInfo_Source_Path) ? camInfo.CamInfo_Source_Path : null;
#if USE_STREAM_TLS
                camera.rtps = (camera.RtspUrlRaw ?? camera.RtspUrlMainRaw ?? directFallback)?.Replace("rtsp://", "rtsps://");
#else
                camera.rtps = camera.RtspUrlRaw ?? camera.RtspUrlMainRaw ?? directFallback;
#endif

                camActives.Add(camera);
            }

            // 2. Build Hierarchical Tree
            var rootGroups = new List<VMTalkGroup>();
            var camMap = camActives.ToDictionary(c => c.camID, c => c);
            var comparer = new NaturalStringComparer();

            foreach (var camInfo in lsCamInfoActive)
            {
                var cam = camMap[camInfo.CamInfo_CamId];
                if (camInfo.Device_Role == "central_radio") continue; 
                // REMOVED: if (cam.is_Master) continue; // Allow all cameras assigned to talk groups

                // The camera sidebar is grouped by the API unit name. Do not
                // use talk_group_path here: that hierarchy is for a different
                // business view and causes cameras to appear under the wrong
                // client/unit in Live View.
                var unitName = !string.IsNullOrWhiteSpace(camInfo.Unit_Name)
                    ? camInfo.Unit_Name.Trim()
                    : (!string.IsNullOrWhiteSpace(camInfo.Unit_Id) ? camInfo.Unit_Id.Trim() : "None");
                var unitId = !string.IsNullOrWhiteSpace(camInfo.Unit_Id)
                    ? camInfo.Unit_Id.Trim() : unitName;
                var unitGroup = rootGroups.FirstOrDefault(g => string.Equals(g.name, unitName, StringComparison.OrdinalIgnoreCase));
                if (unitGroup == null)
                {
                    unitGroup = new VMTalkGroup { name = unitName, groupID = unitId };
                    rootGroups.Add(unitGroup);
                }
                unitGroup.Cameras.Add(cam);
            }

            // Sort everything recursively
            SortGroupsRecursive(rootGroups, comparer);

            foreach (var g in rootGroups)
            {
                g.NotifyItemsChanged(); // Ensure UI refresh for root items
                CamGroupList.Add(g);
            }
        }

        private static bool IsDirectRtspUrl(string value)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out uri))
                return false;
            if (!string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase))
                return false;
            var host = uri.Host ?? string.Empty;
            return host.Length > 0 && host.IndexOf("mediamtx", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsUsableRelayEndpoint(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("mediamtx", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void SortGroupsRecursive(List<VMTalkGroup> groups, NaturalStringComparer comparer)
        {
            if (groups == null || groups.Count == 0) return;

            // Sort current level groups
            var sortedList = groups.OrderBy(g => g.name, comparer).ToList();
            groups.Clear();
            groups.AddRange(sortedList);

            foreach (var group in groups)
            {
                // Sort cameras in this group
                if (group.Cameras != null && group.Cameras.Count > 1)
                {
                    var sortedCams = group.Cameras.OrderBy(c => c.name, comparer).ToList();
                    group.Cameras.Clear();
                    foreach (var c in sortedCams) group.Cameras.Add(c);
                }

                // Recurse sub-groups
                var subList = group.SubGroups.ToList();
                SortGroupsRecursive(subList, comparer);
                
                group.SubGroups.Clear();
                foreach (var sg in subList) group.SubGroups.Add(sg);

                group.NotifyItemsChanged(); // Ensure UI refresh for 'Items'
            }
        }
    }
}















