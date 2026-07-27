using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Gst;
using Gst.App;
using GLib;
using Newtonsoft.Json.Linq;
using V3SClient.libs;

namespace V3SClient.models
{
    public class PlaybackHLS : RtspPlayer
    {
        private string hlsUrl { get; set; }
        private readonly string _sourceHlsUrl;
        private readonly object _snapshotFrameSync = new object();
        private byte[] _latestSnapshotBgr;
        private int _latestSnapshotWidth;
        private int _latestSnapshotHeight;
        private int _latestSnapshotStride;
        private readonly object _hlsAiSync = new object();
        private readonly HashSet<string> _loadedHlsAiSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<HlsAiFrame> _hlsAiFrames = new List<HlsAiFrame>();
        private readonly List<CapturedHlsAiSegment> _capturedHlsAiSegments = new List<CapturedHlsAiSegment>();
        private List<HlsAiSegment> _hlsAiSegments = new List<HlsAiSegment>();
        private readonly HttpClient _hlsAiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private Func<System.DateTime> _playbackClockProvider;
        private CancellationTokenSource _hlsAiCancellation;
        private System.Threading.Tasks.Task _hlsAiLoadTask;
        private Timer _hlsAiRenderTimer;
        private Func<double, System.DateTime> _playbackTimeResolver;
        private PlaybackHlsAiProxy _hlsAiProxy;
        private int _hlsAiParseScheduled;
        private long _lastRenderedAiTimestampMs;

        /// <summary>Timeline information supplied by ViewCameraPlayback after it parses the HLS playlist.</summary>
        public sealed class HlsAiSegment
        {
            public string Url { get; set; }
            public System.DateTime StartTime { get; set; }
            public double DurationSeconds { get; set; }
        }

        private sealed class HlsAiFrame
        {
            public long TimestampMs { get; set; }
            public string SourceUrl { get; set; }
            public List<MetaAIResult> Results { get; set; }
        }

        private sealed class CapturedHlsAiSegment
        {
            public string Url { get; set; }
            public System.DateTime StartTime { get; set; }
            public byte[] Bytes { get; set; }
        }

        /// <summary>
        /// True when the low-rate appsink branch has received an original decoded
        /// camera frame.  This branch is independent of the size of the WPF tile.
        /// </summary>
        public bool HasDecodedSnapshotFrame
        {
            get
            {
                lock (_snapshotFrameSync)
                    return _latestSnapshotBgr != null && _latestSnapshotWidth > 0 && _latestSnapshotHeight > 0;
            }
        }

        protected override bool ForceAspectRatio
        {
            get { return true; }
        }

        public PlaybackHLS(string hlsUrl, IntPtr windowHandle, bool is_h264,
            bool isNvidiaGPU = false, int gpuIdSink = 0) 
            : base(hlsUrl, windowHandle, is_h264, isNvidiaGPU, gpuIdSink)
        {
            this.hlsUrl = hlsUrl;
            _sourceHlsUrl = hlsUrl;
            ShowVideoSlider = System.Windows.Visibility.Visible;
            QueryPositionPlaying();
        }

        /// <summary>
        /// HLS playback stores AI JSON inside the fragment body, not in decoder
        /// SEI.  The view already has the authoritative playlist timeline, so it
        /// passes it here before the pipeline starts. The loopback proxy observes
        /// exactly the responses consumed by hlsdemux, without a second AI fetch.
        /// </summary>
        public void ConfigureHlsAiMetadata(IEnumerable<HlsAiSegment> segments, Func<double, System.DateTime> playbackTimeResolver)
        {
            var baseUri = new System.Uri(_sourceHlsUrl, System.UriKind.Absolute);
            var normalized = (segments ?? Enumerable.Empty<HlsAiSegment>())
                .Where(segment => segment != null && !string.IsNullOrWhiteSpace(segment.Url) && segment.StartTime != System.DateTime.MinValue)
                .Select(segment => new HlsAiSegment
                {
                    Url = new System.Uri(baseUri, segment.Url).AbsoluteUri,
                    StartTime = segment.StartTime,
                    DurationSeconds = Math.Max(0.1, segment.DurationSeconds)
                })
                .OrderBy(segment => segment.StartTime)
                .ToList();

            lock (_hlsAiSync)
            {
                _hlsAiSegments = normalized;
                _playbackTimeResolver = playbackTimeResolver;
                _loadedHlsAiSegments.Clear();
                _hlsAiFrames.Clear();
                _capturedHlsAiSegments.Clear();
                _lastRenderedAiTimestampMs = 0;
            }

            StopHlsAiMetadataLoader();
            if (normalized.Count == 0 || playbackTimeResolver == null)
                return;

            // Keep the decoder on the original server URL. Some recorded HLS
            // playlists use byte-range/fMP4 responses and cannot safely pass a
            // generic loopback proxy. AI is therefore fetched only on demand.
            _playbackClockProvider = GetCurrentHlsAiPlaybackTime;
            _hlsAiCancellation = new CancellationTokenSource();
            _hlsAiLoadTask = System.Threading.Tasks.Task.Run(() => LoadHlsAiMetadataAsync(_hlsAiCancellation.Token));
        }

        public void StopHlsAiMetadata()
        {
            StopHlsAiMetadataLoader();
            lock (_hlsAiSync)
            {
                _hlsAiFrames.Clear();
                _capturedHlsAiSegments.Clear();
                _loadedHlsAiSegments.Clear();
                _lastRenderedAiTimestampMs = 0;
            }
        }

        private System.DateTime GetCurrentHlsAiPlaybackTime()
        {
            if (player == null || _playbackTimeResolver == null)
                return System.DateTime.MinValue;

            long position;
            return player.QueryPosition(Gst.Format.Time, out position)
                ? _playbackTimeResolver(position / (double)Gst.Constants.SECOND)
                : System.DateTime.MinValue;
        }

        private void OnHlsFragmentReceived(string url, byte[] bytes)
        {
            if (!AiOverlayEnabled || bytes == null || bytes.Length == 0) return;

            HlsAiSegment segment;
            lock (_hlsAiSync)
            {
                segment = _hlsAiSegments.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
                if (segment == null || _loadedHlsAiSegments.Contains(url)) return;
                _loadedHlsAiSegments.Add(url);
            }

            // hls.js extracts the JSON immediately before it buffers the media
            // fragment. Do the same when the overlay size is known: bbox data is
            // ready before this fragment can be presented by d3d11videosink.
            if (AiOverlayEnabled && OverlayFrameWidth > 0 && OverlayFrameHeight > 0)
            {
                var frames = ParseHlsAiFrames(bytes, segment.StartTime, segment.Url);
                if (frames.Count > 0)
                {
                    lock (_hlsAiSync)
                    {
                        AddHlsAiFramesLocked(frames, 2400);
                    }
                }
                return;
            }

            lock (_hlsAiSync)
            {
                _capturedHlsAiSegments.Add(new CapturedHlsAiSegment { Url = url, StartTime = segment.StartTime, Bytes = bytes });
                if (_capturedHlsAiSegments.Count > 48)
                    _capturedHlsAiSegments.RemoveRange(0, _capturedHlsAiSegments.Count - 48);
            }
            ScheduleCapturedHlsAiParsing();
        }

        private void ScheduleCapturedHlsAiParsing()
        {
            if (!AiOverlayEnabled || OverlayFrameWidth <= 0 || OverlayFrameHeight <= 0 ||
                Interlocked.Exchange(ref _hlsAiParseScheduled, 1) != 0)
                return;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    List<CapturedHlsAiSegment> captured;
                    lock (_hlsAiSync)
                    {
                        captured = _capturedHlsAiSegments.ToList();
                        _capturedHlsAiSegments.Clear();
                    }

                    var frames = new List<HlsAiFrame>();
                    foreach (var fragment in captured)
                        frames.AddRange(ParseHlsAiFrames(fragment.Bytes, fragment.StartTime, fragment.Url));

                    if (frames.Count == 0) return;
                    lock (_hlsAiSync)
                    {
                        AddHlsAiFramesLocked(frames, 2400);
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "KhÃ´ng thá»ƒ parse AI metadata tá»« HLS fragment Ä‘Ã£ buffer");
                }
                finally
                {
                    Interlocked.Exchange(ref _hlsAiParseScheduled, 0);
                }
            });
        }

        private async System.Threading.Tasks.Task LoadHlsAiMetadataAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Do not duplicate HLS fragment traffic while AI is off. This
                    // keeps ordinary playback identical to the old pipeline.
                    if (!AiOverlayEnabled)
                    {
                        await System.Threading.Tasks.Task.Delay(250, token).ConfigureAwait(false);
                        continue;
                    }

                    // d3d11overlay reports its native texture size on the first
                    // draw callback. Do not consume a segment before boxes can be
                    // scaled to that texture, otherwise it would be cached empty.
                    if (OverlayFrameWidth <= 0 || OverlayFrameHeight <= 0)
                    {
                        await System.Threading.Tasks.Task.Delay(150, token).ConfigureAwait(false);
                        continue;
                    }

                    Func<System.DateTime> clock;
                    List<HlsAiSegment> candidates;
                    lock (_hlsAiSync)
                    {
                        clock = _playbackClockProvider;
                        var now = clock == null ? System.DateTime.MinValue : clock();
                        // Match the Web player's HLS buffering behaviour: retain a
                        // little history for a late decoder frame and prefetch the
                        // next 24 seconds before the video cursor reaches it.  The
                        // previous two-second window was the reason a fragment was
                        // often parsed after its video had already been displayed.
                        candidates = now == System.DateTime.MinValue
                            ? new List<HlsAiSegment>()
                            : _hlsAiSegments
                                .Where(segment =>
                                    segment.StartTime.AddSeconds(segment.DurationSeconds) >= now.AddSeconds(-3) &&
                                    segment.StartTime <= now.AddSeconds(6))
                                .OrderBy(segment => Math.Abs((segment.StartTime - now).TotalSeconds))
                                .Take(2)
                                .ToList();
                    }

                    foreach (var segment in candidates)
                    {
                        token.ThrowIfCancellationRequested();
                        bool alreadyLoaded;
                        lock (_hlsAiSync) alreadyLoaded = _loadedHlsAiSegments.Contains(segment.Url);
                        if (alreadyLoaded) continue;

                        var bytes = await _hlsAiClient.GetByteArrayAsync(segment.Url).ConfigureAwait(false);
                        var frames = ParseHlsAiFrames(bytes, segment.StartTime, segment.Url);
                        lock (_hlsAiSync)
                        {
                            _loadedHlsAiSegments.Add(segment.Url);
                            AddHlsAiFramesLocked(frames, 2400);
                        }
                        if (frames.Count > 0)
                            LoggerManager.LogInfo("Playback HLS AI: loaded " + frames.Count + " frame(s) from " + segment.Url);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "Không thể đọc AI metadata từ HLS playback");
                }

                try { await System.Threading.Tasks.Task.Delay(350, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        // Legacy polling entry point retained for binary compatibility only.
        // ConfigureHlsAiMetadata no longer creates its timer.
        private void RenderHlsAiForCurrentPosition()
        {
            if (!AiOverlayEnabled)
                return;

            try
            {
                ScheduleCapturedHlsAiParsing();
                Func<System.DateTime> clock;
                HlsAiFrame selected;
                lock (_hlsAiSync)
                {
                    clock = _playbackClockProvider;
                    var current = clock == null ? System.DateTime.MinValue : clock();
                    if (current == System.DateTime.MinValue) return;
                    var targetMs = new DateTimeOffset(current).ToUnixTimeMilliseconds();
                    selected = _hlsAiFrames
                        .Where(frame => frame.TimestampMs <= targetMs && targetMs - frame.TimestampMs <= 3000)
                        .OrderByDescending(frame => frame.TimestampMs)
                        .FirstOrDefault();
                }

                if (selected != null && selected.Results != null && selected.Results.Count > 0)
                    Send2Draw(selected.Results);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể đồng bộ AI metadata với playback");
            }
        }

        private void RenderHlsAiForVideoPosition(double videoPositionSeconds)
        {
            if (!AiOverlayEnabled)
                return;

            try
            {
                ScheduleCapturedHlsAiParsing();
                HlsAiFrame selected;
                lock (_hlsAiSync)
                {
                    var current = _playbackTimeResolver == null
                        ? System.DateTime.MinValue
                        : _playbackTimeResolver(videoPositionSeconds);
                    if (current == System.DateTime.MinValue)
                        return;

                    var targetMs = new DateTimeOffset(current).ToUnixTimeMilliseconds();
                    selected = _hlsAiFrames
                        .Where(frame => frame.TimestampMs <= targetMs && targetMs - frame.TimestampMs <= 3000)
                        .OrderByDescending(frame => frame.TimestampMs)
                        .FirstOrDefault();
                }

                if (selected != null && selected.TimestampMs != _lastRenderedAiTimestampMs &&
                    selected.Results != null && selected.Results.Count > 0)
                {
                    _lastRenderedAiTimestampMs = selected.TimestampMs;
                    Send2Draw(selected.Results);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể đồng bộ AI metadata với frame playback");
            }
        }

        // Keep a bounded frame cache without falsely marking evicted segments as
        // loaded. This is essential when the user seeks back several minutes.
        private void AddHlsAiFramesLocked(IEnumerable<HlsAiFrame> frames, int maxFrames)
        {
            _hlsAiFrames.AddRange(frames);
            _hlsAiFrames.Sort((left, right) => left.TimestampMs.CompareTo(right.TimestampMs));
            if (_hlsAiFrames.Count <= maxFrames) return;

            var removeCount = _hlsAiFrames.Count - maxFrames;
            var evictedUrls = _hlsAiFrames.Take(removeCount)
                .Select(frame => frame.SourceUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _hlsAiFrames.RemoveRange(0, removeCount);
            foreach (var url in evictedUrls)
            {
                if (!_hlsAiFrames.Any(frame => string.Equals(frame.SourceUrl, url, StringComparison.OrdinalIgnoreCase)))
                    _loadedHlsAiSegments.Remove(url);
            }
        }

        private List<HlsAiFrame> ParseHlsAiFrames(byte[] bytes, System.DateTime segmentStartTime, string sourceUrl)
        {
            var frames = new List<HlsAiFrame>();
            if (bytes == null || bytes.Length == 0 || OverlayFrameWidth <= 0 || OverlayFrameHeight <= 0)
                return frames;

            var text = Encoding.UTF8.GetString(bytes);
            var cursor = 0;
            while (cursor < text.Length)
            {
                var marker = text.IndexOf("\"ai_results\"", cursor, StringComparison.Ordinal);
                if (marker < 0) break;
                JObject payload = null;
                var end = -1;
                for (var start = text.LastIndexOf('{', marker); start >= 0; start = text.LastIndexOf('{', start - 1))
                {
                    end = FindJsonObjectEnd(text, start);
                    if (end < 0) continue;
                    try
                    {
                        var candidate = JObject.Parse(text.Substring(start, end - start + 1));
                        if (candidate["ai_results"] is JArray) { payload = candidate; break; }
                    }
                    catch { }
                }
                cursor = end > marker ? end + 1 : marker + 12;
                if (payload == null) continue;

                var results = BuildHlsAiResults(payload);
                if (results.Count == 0) continue;
                var timestamp = GetPayloadTimestampMs(payload);
                if (timestamp <= 0)
                    timestamp = new DateTimeOffset(segmentStartTime).ToUnixTimeMilliseconds();
                frames.Add(new HlsAiFrame { TimestampMs = timestamp, SourceUrl = sourceUrl, Results = results });
            }
            return frames;
        }

        private static int FindJsonObjectEnd(string text, int start)
        {
            var depth = 0; var quoted = false; var escaped = false;
            for (var index = start; index < text.Length; index++)
            {
                var value = text[index];
                if (quoted) { if (escaped) escaped = false; else if (value == '\\') escaped = true; else if (value == '"') quoted = false; continue; }
                if (value == '"') quoted = true;
                else if (value == '{') depth++;
                else if (value == '}' && --depth == 0) return index;
            }
            return -1;
        }

        // This intentionally maps the permissive HLS JSON schema used by the
        // Web player, rather than deserializing it into the stricter SEI model.
        // HLS can use image_width/image_height, x/y bbox coordinates and an
        // array in detected_object_ids.
        private List<MetaAIResult> BuildHlsAiResults(JObject payload)
        {
            var image = payload == null ? null : payload["image"] as JObject;
            var sourceWidth = JsonInt(image == null ? null : image["width"] ?? image["image_width"], JsonInt(payload == null ? null : payload["width"], 0));
            var sourceHeight = JsonInt(image == null ? null : image["height"] ?? image["image_height"], JsonInt(payload == null ? null : payload["height"], 0));
            var objects = payload == null ? null : payload["ai_results"] as JArray;
            if (sourceWidth <= 0 || sourceHeight <= 0 || objects == null)
                return new List<MetaAIResult>();

            var scaleX = (float)OverlayFrameWidth / sourceWidth;
            var scaleY = (float)OverlayFrameHeight / sourceHeight;
            var minConfidence = GlobalSystem.Instance.MinConfidence;
            var results = new List<MetaAIResult>();
            var index = 0;
            foreach (var token in objects)
            {
                index++;
                var item = token as JObject;
                var bbox = item == null ? null : item["bbox"] as JObject;
                if (item == null || bbox == null) continue;

                var label = JsonString(item["meta_type"] ?? item["label"]) ?? "object";
                var objectId = JsonString(item["detected_object_ids"]);
                var name = JsonString(item["name"]) ?? objectId ?? label;
                if (IsNormal(name) || IsNormal(objectId) || IsNormal(label)) continue;

                var confidence = JsonFloat(item["confidence"]);
                if (confidence < minConfidence) continue;
                var left = JsonFloat(bbox["left"] ?? bbox["x"]);
                var top = JsonFloat(bbox["top"] ?? bbox["y"]);
                var width = JsonFloat(bbox["width"]);
                var height = JsonFloat(bbox["height"]);
                if (width <= 0 || height <= 0) continue;

                var result = new MetaAIResult(new SharpDX.RectangleF(left * scaleX, top * scaleY, width * scaleX, height * scaleY),
                    name, JsonBool(item["is_blacklist"]), true, objectId, JsonString(item["event_type"]),
                    JsonString(item["object_image"]), JsonString(item["time_stamp"]), label,
                    JsonString(item["tracking_object_id"] ?? item["tracking_id"]) ?? index.ToString(), null, confidence);
                ApplyRoiDwellInfo(result, item["object_analysis"]);
                results.Add(result);
            }
            return results;
        }

        private static long GetPayloadTimestampMs(JObject payload)
        {
            long timestamp;
            if (long.TryParse(JsonString(payload == null ? null : payload["ntp_timestamp"]), out timestamp))
                return ToUnixMilliseconds(timestamp);
            DateTimeOffset producedAt;
            return DateTimeOffset.TryParse(JsonString(payload == null ? null : payload["produced_at"]), out producedAt)
                ? producedAt.ToUnixTimeMilliseconds() : 0;
        }

        private static void ApplyRoiDwellInfo(MetaAIResult result, JToken analysisToken)
        {
            var analysis = analysisToken as JObject ?? (analysisToken as JArray)?.OfType<JObject>().FirstOrDefault();
            var inside = analysis == null ? null : analysis["roi_inside"] as JObject;
            var dwell = analysis == null ? null : analysis["roi_dwell_seconds"] as JObject;
            if (inside == null || dwell == null) return;
            foreach (var roi in inside.Properties().Where(property => JsonBool(property.Value)))
            {
                var seconds = JsonFloat(dwell[roi.Name]);
                result.RoiDwellSecondsInfo = seconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";
                result.RoiId = roi.Name;
                return;
            }
        }

        private static bool IsNormal(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Split(',', ';', '|')
                .Any(part => string.Equals(part.Trim(' ', '"', '[', ']'), "normal", StringComparison.OrdinalIgnoreCase));
        }

        private static string JsonString(JToken token)
        {
            var array = token as JArray;
            if (array != null) return string.Join(",", array.Select(JsonString).Where(value => !string.IsNullOrWhiteSpace(value)));
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        private static float JsonFloat(JToken token)
        {
            float value;
            return float.TryParse(JsonString(token), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private static int JsonInt(JToken token, int fallback)
        {
            int value;
            return int.TryParse(JsonString(token), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool JsonBool(JToken token)
        {
            bool value;
            return bool.TryParse(JsonString(token), out value) && value;
        }

        private static long ToUnixMilliseconds(long timestamp)
        {
            if (timestamp > 10000000000000000L) return timestamp / 1000000L;
            if (timestamp > 10000000000000L) return timestamp / 1000L;
            return timestamp;
        }

        private void StopHlsAiMetadataLoader()
        {
            var cancellation = _hlsAiCancellation;
            _hlsAiCancellation = null;
            if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); }
            _hlsAiRenderTimer?.Dispose();
            _hlsAiRenderTimer = null;
        }

        private PadProbeReturn GetPlaybackFrameInfo(Pad pad, PadProbeInfo info)
        {
            var buffer = info.Buffer;
            if (buffer != null && buffer.Pts != Gst.Constants.CLOCK_TIME_NONE)
                RenderHlsAiForVideoPosition(buffer.Pts / (double)Gst.Constants.SECOND);

            return GetFrameInfo(pad, info);
        }

        protected override void OnSeek(long targetTime)
        {
            // A flushing seek clears the renderer's previous frame. Reset the
            // timestamp guard too, otherwise returning to an already rendered
            // AI timestamp leaves the new frame without any bbox.
            lock (_hlsAiSync)
                _lastRenderedAiTimestampMs = 0;
            ClearPendingAiDraws();
            base.OnSeek(targetTime);
        }

        protected override SeekFlags GetSeekFlags()
        {
            // KeyUnit displays the preceding GOP before reaching the requested
            // timeline point. Accurate keeps that decode internal and presents
            // only the selected recorded frame.
            return SeekFlags.Flush | SeekFlags.Accurate;
        }

        protected override void ReConnect()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                if (this.player != null)
                {
                    this.player.SetState(State.Null);
                }
                CommandToSupper(PlayerStatus.Stop);
            });
        }

        protected override string GetPipelineDescription()
        {
            return "";
        }

        public override bool InitPipeline()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            bool ret = this.CreatePipeline();
            if (!ret) return false;

            player.Bus.EnableSyncMessageEmission();
            player.Bus.SyncMessage += Monitor;

            // Playback intentionally does not consume AI/SEI metadata.  It must
            // remain a video-only pipeline so its stability is independent of
            // the AI service and the number of open cameras.

            this.player.SetState(State.Playing);
            return ret;
        }

        protected override bool CreatePipeline()
        {
            try
            {
                if (player != null)
                    this.Dispose();

                player = new Pipeline("hls-pipeline");

                // 1. Create Base Elements
                videoSource = ElementFactory.Make("souphttpsrc", "videoSource");
                if (!string.IsNullOrEmpty(hlsUrl))
                    videoSource["location"] = hlsUrl;
                videoSource["ssl-use-system-ca-file"] = true;
                videoSource["ssl-strict"] = true;

                Element hlsDemux = ElementFactory.Make("hlsdemux", "hlsDemux");
                demux = ElementFactory.Make("parsebin", "tsDemux");

                player.Add(videoSource, hlsDemux, demux);
                videoSource.Link(hlsDemux);

                // 2. Dynamic HLS -> TS Link
                hlsDemux.PadAdded += (sender, args) =>
                {
                    Pad hlsSrcPad = args.NewPad;
                    Pad tsSinkPad = demux.GetStaticPad("sink");
                    if (!tsSinkPad.IsLinked) hlsSrcPad.Link(tsSinkPad);
                    tsSinkPad?.Dispose();
                };

                // 3. Dynamic TS -> Video/Audio Link
                demux.PadAdded += (sender, args) =>
                {
                    Pad newPad = args.NewPad;
                    Caps caps = newPad.QueryCaps();

                    if (caps != null && caps.Size > 0)
                    {
                        string capsName = caps.GetStructure(0).Name;

                        // --- VIDEO BRANCH ---
                        if (capsName.StartsWith("video/x-h264", StringComparison.OrdinalIgnoreCase) ||
                            capsName.StartsWith("video/x-h265", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isH265 = capsName.StartsWith("video/x-h265", StringComparison.OrdinalIgnoreCase);
                            this.IsH264 = !isH265;

                            string parseName = isH265 ? "h265parse" : "h264parse";
                            string decName = isH265 ? "d3d11h265dec" : "d3d11h264dec";

                            videoQueue = ElementFactory.Make("queue", "video-queue");
                            // Removed leaky=1 to prevent dropping HLS chunks
                            
                            Element vParse = ElementFactory.Make(parseName, "video-parse");
                            identity = ElementFactory.Make("identity", "identity");

                            Element decoder = ElementFactory.Make(decName, "video-decoder");
                            decoder["qos"] = false;

                            Element converter = ElementFactory.Make("d3d11convert", "video-converter");
                            videoOverlay = ElementFactory.Make("d3d11overlay", "videoOverlay");

                            Element vSink = ElementFactory.Make("d3d11videosink", "video-sink");
                            vSink["async"] = true;
                            vSink["sync"] = true;
                            vSink["qos"] = true;
                            vSink["force-aspect-ratio"] = true;

                            player.Add(videoQueue, vParse, identity, decoder, converter, videoOverlay, vSink);

                            videoQueue.SyncStateWithParent();
                            vParse.SyncStateWithParent();
                            identity.SyncStateWithParent();
                            decoder.SyncStateWithParent();
                            converter.SyncStateWithParent();
                            videoOverlay.SyncStateWithParent();
                            vSink.SyncStateWithParent();

                            Element.Link(videoQueue, vParse, identity, decoder, converter, videoOverlay, vSink);

                            videoOverlay.Connect("draw", Draw);
                            Pad identity_src = identity.GetStaticPad("src");
                            identity_src.AddProbe(Gst.PadProbeType.Buffer, GetPlaybackFrameInfo);
                            identity_src.Dispose();

                            Pad vQueueSinkPad = videoQueue.GetStaticPad("sink");
                            newPad.Link(vQueueSinkPad);
                            vQueueSinkPad.Dispose();
                        }
                        // --- AUDIO BRANCH ---
                        else if (capsName.StartsWith("audio/"))
                        {
                            audioQueue = ElementFactory.Make("queue", "audio-queue");
                            // Removed leaky=1 to prevent dropping audio chunks

                            Element aDecodeBin = ElementFactory.Make("decodebin", "audio-decodebin");
                            Element aConvert = ElementFactory.Make("audioconvert", "audio-convert");
                            Element aResample = ElementFactory.Make("audioresample", "audio-resample");

                            audioVolume = ElementFactory.Make("volume", "audioVolume");
                            audioVolume["volume"] = 0;

                            Element aSink = ElementFactory.Make("wasapisink", "audio-sink");
                            aSink["async"] = true;
                            aSink["sync"] = true;

                            player.Add(audioQueue, aDecodeBin, aConvert, aResample, audioVolume, aSink);

                            audioQueue.SyncStateWithParent();
                            aDecodeBin.SyncStateWithParent();
                            aConvert.SyncStateWithParent();
                            aResample.SyncStateWithParent();
                            audioVolume.SyncStateWithParent();
                            aSink.SyncStateWithParent();

                            audioQueue.Link(aDecodeBin);

                            aDecodeBin.PadAdded += (dbSender, dbArgs) =>
                            {
                                Pad dbPad = dbArgs.NewPad;
                                Pad convertSinkPad = aConvert.GetStaticPad("sink");
                                if (!convertSinkPad.IsLinked) dbPad.Link(convertSinkPad);
                                convertSinkPad?.Dispose();
                            };

                            Element.Link(aConvert, aResample, audioVolume, aSink);

                            Pad aQueueSinkPad = audioQueue.GetStaticPad("sink");
                            newPad.Link(aQueueSinkPad);
                            aQueueSinkPad.Dispose();
                        }
                    }
                    caps?.Dispose();
                };

                LoggerManager.GeneralLog(NLog.LogLevel.Info, $" Playback created OK");
                return true;
            }
            catch (Exception e)
            {
                LoggerManager.GeneralLog(NLog.LogLevel.Error, $"Playback error create pipeline fail: {e.ToString()}");
                return false;
            }
        }



        private void SnapshotSink_NewSample(object sender, NewSampleArgs args)
        {
            var sink = sender as AppSink;
            if (sink == null)
                return;

            Sample sample = null;
            try
            {
                sample = sink.PullSample();
                if (sample == null || sample.Buffer == null || sample.Caps == null || sample.Caps.Size == 0)
                    return;

                var structure = sample.Caps.GetStructure(0);
                int width;
                int height;
                if (structure == null || !structure.GetInt("width", out width) || !structure.GetInt("height", out height) ||
                    width <= 0 || height <= 0)
                    return;

                var buffer = sample.Buffer;
                MapInfo map;
                if (!buffer.Map(out map, MapFlags.Read) || map.Data == null || map.Data.Length == 0)
                    return;

                try
                {
                    var stride = map.Data.Length / height;
                    if (stride < width * 3)
                        return;

                    var frame = new byte[map.Data.Length];
                    System.Buffer.BlockCopy(map.Data, 0, frame, 0, frame.Length);
                    lock (_snapshotFrameSync)
                    {
                        _latestSnapshotBgr = frame;
                        _latestSnapshotWidth = width;
                        _latestSnapshotHeight = height;
                        _latestSnapshotStride = stride;
                    }
                }
                finally
                {
                    buffer.Unmap(map);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("Không thể nhận frame snapshot playback: " + ex.Message);
            }
            finally
            {
                sample?.Dispose();
            }
        }

        /// <summary>
        /// Saves the latest decoded frame at its source resolution.  The appsink
        /// branch is intentionally throttled to 1 fps, keeping CPU/GPU use low while
        /// still making a current Full HD snapshot available for user actions.
        /// </summary>
        public bool TrySaveDecodedSnapshot(string outputPath)
        {
            byte[] frame;
            int width;
            int height;
            int stride;
            lock (_snapshotFrameSync)
            {
                if (_latestSnapshotBgr == null || _latestSnapshotWidth <= 0 || _latestSnapshotHeight <= 0)
                    return false;

                frame = _latestSnapshotBgr;
                width = _latestSnapshotWidth;
                height = _latestSnapshotHeight;
                stride = _latestSnapshotStride;
            }

            try
            {
                using (var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        var sourceRowBytes = width * 3;
                        for (var row = 0; row < height; row++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(frame, row * stride,
                                IntPtr.Add(data.Scan0, row * data.Stride), sourceRowBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    if (AiOverlayEnabled)
                        DrawAiOnSnapshot(bitmap);
                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể lưu frame gốc playback");
                return false;
            }
        }

        /// <summary>
        /// The appsink carries an unscaled camera frame while d3d11overlay carries
        /// boxes in native render coordinates. Repaint the same current AI data on
        /// the source bitmap so exported snapshots stay at camera resolution.
        /// </summary>
        private void DrawAiOnSnapshot(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null || OverlayFrameWidth <= 0 || OverlayFrameHeight <= 0)
                return;

            var results = GetCurrentAiDrawResults();
            if (results.Count == 0)
                return;

            var scaleX = bitmap.Width / (float)OverlayFrameWidth;
            var scaleY = bitmap.Height / (float)OverlayFrameHeight;
            var lineWidth = Math.Max(2f, 2.5f * Math.Min(scaleX, scaleY));
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif,
                Math.Max(11f, 12f * Math.Min(scaleX, scaleY)), System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Pixel))
            using (var format = new System.Drawing.StringFormat(System.Drawing.StringFormatFlags.NoWrap |
                System.Drawing.StringFormatFlags.NoClip))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Same two-pass ordering as the onscreen overlay: all boxes first,
                // labels afterwards so one bbox never covers another label.
                foreach (var item in results)
                {
                    if (item == null || !item.IsDisplay) continue;
                    var box = item.BoundingBox;
                    var rect = new System.Drawing.RectangleF(box.Left * scaleX, box.Top * scaleY,
                        box.Width * scaleX, box.Height * scaleY);
                    using (var pen = new System.Drawing.Pen(GetAiSnapshotColor(item), lineWidth))
                        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }

                foreach (var item in results)
                {
                    if (item == null || !item.IsDisplay) continue;
                    var box = item.BoundingBox;
                    var rect = new System.Drawing.RectangleF(box.Left * scaleX, box.Top * scaleY,
                        box.Width * scaleX, box.Height * scaleY);
                    var label = GetAiSnapshotLabel(item);
                    var labelSize = graphics.MeasureString(label, font, int.MaxValue, format);
                    var labelHeight = (float)Math.Ceiling(labelSize.Height) + 4;
                    var labelWidth = (float)Math.Ceiling(labelSize.Width) + 8;
                    var labelY = Math.Max(0, rect.Y - labelHeight);
                    var labelRect = new System.Drawing.RectangleF(rect.X, labelY, labelWidth, labelHeight);
                    using (var background = new System.Drawing.SolidBrush(GetAiSnapshotColor(item)))
                    using (var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(3, 19, 10)))
                    {
                        graphics.FillRectangle(background, labelRect);
                        graphics.DrawString(label, font, foreground, rect.X + 4, labelY + 2, format);
                    }
                }
            }
        }

        /// <summary>Applies the current overlay to an already exported source PNG.</summary>
        public bool TryDrawAiOnSnapshotFile(string outputPath)
        {
            if (!AiOverlayEnabled || string.IsNullOrWhiteSpace(outputPath) || !System.IO.File.Exists(outputPath))
                return false;

            var temporaryPath = outputPath + ".ai.tmp";
            try
            {
                using (var bitmap = new System.Drawing.Bitmap(outputPath))
                {
                    DrawAiOnSnapshot(bitmap);
                    bitmap.Save(temporaryPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                System.IO.File.Copy(temporaryPath, outputPath, true);
                System.IO.File.Delete(temporaryPath);
                return true;
            }
            catch (Exception ex)
            {
                try { if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath); }
                catch { }
                LoggerManager.LogException(ex, "KhÃ´ng thá»ƒ vẽ AI lên ảnh gốc playback");
                return false;
            }
        }

        protected override void ReleasePipeline()
        {
            StopHlsAiMetadataLoader();
            _hlsAiProxy?.Dispose();
            _hlsAiProxy = null;
            if (player != null)
            {
                player.SetState(State.Paused);
                player.SetState(State.Ready);
                player.SetState(State.Null);
                player.Bus.DisableSyncMessageEmission();
                var children = player.Children;
                foreach (Element child in children)
                    if (child != null)
                        child.Dispose();
            }
            videoSource?.Dispose();
            videoQueue?.Dispose();
            audioVolume?.Dispose();
            videoOverlay?.Dispose();
            player?.Dispose();

            windowHandle = IntPtr.Zero;
          
            player = null;
        }
    }

    /// <summary>
    /// Small loopback HLS proxy. It lets hlsdemux consume one network response
    /// while the AI path observes the exact same fragment bytes, mirroring the
    /// custom hls.js loader used by the Web application.
    /// </summary>
    internal sealed class PlaybackHlsAiProxy : IDisposable
    {
        private readonly Action<string, byte[]> _onFragment;
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(15) };
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private HttpListener _listener;
        private System.Threading.Tasks.Task _listenTask;

        public string EntryUrl { get; private set; }

        public PlaybackHlsAiProxy(Action<string, byte[]> onFragment)
        {
            _onFragment = onFragment;
        }

        public bool Start(string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl)) return false;
            try
            {
                var port = ReservePort();
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
                EntryUrl = BuildProxyUrl(new System.Uri(sourceUrl, System.UriKind.Absolute));
                _listenTask = System.Threading.Tasks.Task.Run(ListenAsync);
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("KhÃ´ng thá»ƒ khá»Ÿi táº¡o HLS AI proxy: " + ex.Message);
                return false;
            }
        }

        private static int ReservePort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async System.Threading.Tasks.Task ListenAsync()
        {
            while (!_cancellation.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext context = null;
                try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                if (context != null)
                    _ = System.Threading.Tasks.Task.Run(() => HandleRequestAsync(context));
            }
        }

        private async System.Threading.Tasks.Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var encodedUrl = context.Request.QueryString["u"];
                System.Uri remote;
                if (string.IsNullOrWhiteSpace(encodedUrl) || !System.Uri.TryCreate(encodedUrl, System.UriKind.Absolute, out remote))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                using (var response = await _client.GetAsync(remote, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    context.Response.StatusCode = (int)response.StatusCode;
                    var mediaType = response.Content.Headers.ContentType == null ? null : response.Content.Headers.ContentType.MediaType;
                    var isPlaylist = !string.IsNullOrWhiteSpace(mediaType) && mediaType.IndexOf("mpegurl", StringComparison.OrdinalIgnoreCase) >= 0;
                    isPlaylist = isPlaylist || remote.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                        Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 16)).Contains("#EXTM3U");

                    if (isPlaylist)
                    {
                        bytes = Encoding.UTF8.GetBytes(RewritePlaylist(Encoding.UTF8.GetString(bytes), remote));
                        context.Response.ContentType = "application/vnd.apple.mpegurl";
                    }
                    else
                    {
                        context.Response.ContentType = string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType;
                        try { _onFragment?.Invoke(remote.AbsoluteUri, bytes); }
                        catch (Exception ex) { LoggerManager.LogDebug("KhÃ´ng thá»ƒ tá»« fragment HLS sang AI: " + ex.Message); }
                    }

                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("HLS AI proxy request failed: " + ex.Message);
                try { context.Response.StatusCode = (int)HttpStatusCode.BadGateway; }
                catch { }
            }
            finally
            {
                try { context.Response.Close(); }
                catch { }
            }
        }

        private string RewritePlaylist(string playlist, System.Uri baseUri)
        {
            var rewritten = Regex.Replace(playlist, "(?m)^(?!#)(?<url>[^\\r\\n]+)$", match =>
            {
                System.Uri resolved;
                return System.Uri.TryCreate(baseUri, match.Groups["url"].Value.Trim(), out resolved)
                    ? BuildProxyUrl(resolved) : match.Value;
            });
            return Regex.Replace(rewritten, "URI=\\\"(?<url>[^\\\"]+)\\\"", match =>
            {
                System.Uri resolved;
                return System.Uri.TryCreate(baseUri, match.Groups["url"].Value, out resolved)
                    ? "URI=\\\"" + BuildProxyUrl(resolved) + "\\\"" : match.Value;
            }, RegexOptions.IgnoreCase);
        }

        private string BuildProxyUrl(System.Uri remote)
        {
            var prefix = _listener == null ? string.Empty : _listener.Prefixes.First();
            return prefix + "hls?u=" + System.Uri.EscapeDataString(remote.AbsoluteUri);
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            try { _listener?.Stop(); }
            catch { }
            try { _listener?.Close(); }
            catch { }
            _listener = null;
            _client.Dispose();
            _cancellation.Dispose();
        }
    }
}
