using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gst.Video;
using Gst;
using System.Diagnostics;
using GLib;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows;
using Gst.App;
using System.Runtime.InteropServices;

using OpenCvSharp;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Format = SharpDX.DXGI.Format;
using System.Threading;
using SharpDX.DirectWrite;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;
using V3SClient.libs;
using System.IO;
using V3SClient.TLS;
using System.Windows.Threading;
using System.Windows.Forms;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;


namespace V3SClient.models
{
    public class RtspPlayer : IDisposable
    {

        public bool IsH264 { get; set; } = true; // False nghÄ©a lÃ  H265 mode
        private Dictionary<string, System.DateTime> _warningCache = new Dictionary<string, System.DateTime>();
        private readonly TimeSpan _warningSuppressTime = TimeSpan.FromSeconds(2);
        
        private Dictionary<string, System.DateTime> _aiLogCache = new Dictionary<string, System.DateTime>();
        private readonly TimeSpan _aiLogSuppressTime = TimeSpan.FromSeconds(5);
        private readonly object _pipelineLock = new object();
        public Visibility ShowVideoSlider { get; set; } = Visibility.Collapsed;
        public bool RoiInfoShow { get; set; } = MetaAIResultStorage.Instance.RoiInfoShow;
        private BlockingCollection<List<MetaAIResult>> _aiResult;
        // Short seek is intentionally small for responsive review.  Longer seeks
        // are explicitly requested by the playback toolbar.
        private const long _seekStep = 10;

        public event EventHandler<List<MetaAIResult>> SendMetaAIResult;
        public event EventHandler<PlayerInfo> PlayerSending;        
        protected event EventHandler<byte[]> SendSeiNal;
        public event EventHandler<GMap.NET.PointLatLng> SendGPS;
        public event EventHandler<string> SendWarning;
        protected System.Threading.Timer _timer;


        int widthFrame = 0;
        int heightFrame = 0;
        float _maxRate = 4.0f;

        private HashSet<string> CocoBlacklists;

        private HashSet<string> AllowCocoClass;
        private bool isNvidia_GPU { get; set; }
        protected Element videoSource { get; set; }
         protected Element demux { get; set; }
      
        protected Element identity { get; set; }      
      protected Element videoQueue { get; set; }     
        protected Element videoOverlay { get; set; }
        protected Element audioQueue { get; set; }
        protected Element audioVolume { get; set; }
        protected float currentRate { get; set; } = 1.0f;
        protected float stepRate { get; set; } = 0.3f;
        // Opt-in visual cue for playback capture/download selection. It is drawn
        // by d3d11overlay on the same native texture, avoiding WPF/HWND airspace.
        // Default is false so Live View and existing V3 consumers are unchanged.
        public bool DimForCaptureSelection { get; set; }
        private string rtspAddres { get; set; } = "";
        public Pipeline player { get; set; } = null;
        protected IntPtr windowHandle { get; set; }

        private RawColor4 _redColorBrush;
        private RawColor4 _greenColorBrush;
        private RawColor4 _blueColorBrush;
        private RawColor4 _goldOrangeColorBrush;
        private RawColor4 _roiInfoColorBrush;

        private SharpDX.DirectWrite.TextFormat _textFormat;
        private SharpDX.DirectWrite.Factory _textFactory;

        private SharpDX.Direct2D1.SolidColorBrush _solidColorBrush;
        private SharpDX.Direct2D1.Factory _solidFactory;

        private SharpDX.Direct2D1.RenderTargetProperties _renderTargetProperties;


        int GpuSinkId { get; set; }
        bool _isReconnecting = true;
        public virtual void ParseSeiNalH265(object sender, byte[] data)
        {
            if (data.Length < 5)
                return;

            int offset = 0;
            if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x01)
                offset = 4;
            else if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01)
                offset = 3;
            else
                return;

            // NAL header and type.
            byte firstByte = data[offset];
            int nalUnitType = (firstByte >> 1) & 0x3F; // Extract 6-bit nal_unit_type.
            if (nalUnitType != 39) // Prefix SEI for H.265.
                return;
            int ptr = offset + 2; // Start after start code and 2-byte NAL header.
            // Parse payload type.
            int payloadType = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadType += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;
            payloadType += data[ptr];
            ptr++;
            // Parse payload size.
            int payloadSize = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadSize += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;
            payloadSize += data[ptr];
            ptr++;
            // Verify there is enough data for the payload.
            if (ptr + payloadSize > data.Length) return;
            // Extract the payload.
            byte[] payload = new byte[payloadSize];
            System.Array.Copy(data, ptr, payload, 0, payloadSize);
            // Verify minimum payload length: 16-byte UUID + 4-byte text length.
            if (payload.Length < 20) return;
            // Extract UUID.
            byte[] uuidExtracted = new byte[16];
            System.Array.Copy(payload, 0, uuidExtracted, 0, 16);
            // Extract text length (4 bytes, little-endian).
            uint textLength = System.BitConverter.ToUInt32(payload, 16);
            int expectedPayloadLength = 16 + 4 + (int)textLength;
            if (payload.Length < expectedPayloadLength)
                return;
            // Extract text bytes and decode as UTF-8.
            byte[] textBytes = new byte[textLength];
            System.Array.Copy(payload, 20, textBytes, 0, textLength);
            ExtractAIMeta(textBytes);
        }
        protected virtual void ParseSeiNalH264(object sender, byte[] data)
        {
            if (data.Length < 4) return;
            
            int offset = 0;
            if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x01)
                offset = 4;
            else if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01)
                offset = 3;
            else
                return;

            // NAL header and type.
            byte nalHeader = data[offset];
            int nalUnitType = nalHeader & 0x1F;
            if (nalUnitType != 6) return;
            int ptr = offset + 1;
            // Parse payload type.
            int payloadType = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadType += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;
            payloadType += data[ptr];
            ptr++;
            // Parse payload size.
            int payloadSize = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadSize += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;
            payloadSize += data[ptr];
            ptr++;
            // Verify there is enough data for the payload.
            if (ptr + payloadSize > data.Length) return;
            // Extract the payload.
            byte[] payload = new byte[payloadSize];
            System.Array.Copy(data, ptr, payload, 0, payloadSize);
            // Verify minimum payload length: 16-byte UUID + 4-byte text length.
            if (payload.Length < 20) return;
            // Extract UUID.
            byte[] uuidExtracted = new byte[16];
            System.Array.Copy(payload, 0, uuidExtracted, 0, 16);
            // Extract text length (4 bytes, little-endian).
            uint textLength = System.BitConverter.ToUInt32(payload, 16);
            int expectedPayloadLength = 16 + 4 + (int)textLength;
            if (payload.Length < expectedPayloadLength) return;
            // Extract text bytes and decode UTF-8 text.
            byte[] textBytes = new byte[textLength];
            System.Array.Copy(payload, 20, textBytes, 0, textLength);
            ExtractAIMeta(textBytes);
        }
        private void ExtractAIMeta(byte[] textBytes)
        {
            try
            {
                string text = Encoding.UTF8.GetString(textBytes);
             
                MetaFrame frameInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<MetaFrame>(text);
                if (frameInfo == null) return;
#if VIEW_ONLY
#else
                // Extracting AI results
                if (frameInfo.AiResults != null && frameInfo.AiResults.Count > 0)
                {
                    float scaleX = (float)widthFrame / frameInfo.ImageInfo.ImageWidth;
                    float scaleY = (float)heightFrame / frameInfo.ImageInfo.ImageHeight;
                    
                    var currentAllowClasses = GlobalSystem.Instance.AllowCocoClass;
                    var currentAllowEvents = GlobalSystem.Instance.AllowEvents;
                    float minConf = GlobalSystem.Instance.MinConfidence;

                    string fallbackTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    try
                    {
                        if (frameInfo.NtpTimestamp > 0)
                        {
                            // Convert nanoseconds to milliseconds for DateTimeOffset
                            long ms = frameInfo.NtpTimestamp / 1000000;
                            fallbackTime = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }
                    catch { }

                    List<MetaAIResult> aiResults = frameInfo.AiResults
                        .Where(x => x.Confidence >= minConf) // Filter by confidence
                        .Select(x => {
                            var metaAi = new MetaAIResult(
                                boundingBox: new SharpDX.RectangleF(x.Bbox.Left * scaleX, x.Bbox.Top * scaleY,
                                                   x.Bbox.Width * scaleX, x.Bbox.Height * scaleY),
                                caption: x.Caption,
                                isBlackList: x.IsBlacklist,
                                isDisplay: true,
                                objectID: x.ObjectID,
                                eventType: x.EventType,
                                encodeObjectImage: x.EncodedObjectImage,
                                timeStamp: !string.IsNullOrEmpty(x.TimeStamp) ? x.TimeStamp : fallbackTime,
                                metaType: x.MetaType,
                                trackingObjectIndex: x.TrackingObjectIndex,
                                confidence: x.Confidence);

                            if (x.ObjectAnalysisList != null && x.ObjectAnalysisList.Count > 0)
                            {
                                var analysis = x.ObjectAnalysisList[0];
                                if (analysis.RoiInside != null && analysis.RoiDwellSeconds != null)
                                {
                                    foreach (var kv in analysis.RoiInside)
                                    {
                                        if (kv.Value && analysis.RoiDwellSeconds.ContainsKey(kv.Key))
                                        {
                                            metaAi.RoiDwellSecondsInfo = $"{analysis.RoiDwellSeconds[kv.Key]:F1}s";
                                            break; // Lấy ROI đầu tiên match
                                        }
                                    }
                                }
                            }
                            return metaAi;
                        }).ToList();

                    // Filter for UI Log
                    List<MetaAIResult> logData = aiResults.Where(x => 
                        (currentAllowEvents.Contains(x.EventType?.ToLower()) || x.IsBlackList == true) && 
                        (currentAllowClasses.Contains(x.MetaType?.ToLower()))).ToList();

                    if (logData != null && logData.Count > 0)
                    {
                        // 1. Draw all bounding boxes on the video (Full logData)
                        Send2Draw(logData);

                        // 2. Only send results with object images to the UI Log
                        // Cho phép hiển thị ảnh nếu là BlackList HOẶC thuộc danh sách AllowImageClasses cấu hình riêng
                        var logDataWithImages = logData.Where(x => !string.IsNullOrEmpty(x.EncodeObjectImage) && 
                                                                  (x.IsBlackList == true || GlobalSystem.Instance.AllowImageClasses.Contains(x.MetaType?.ToLower()))).ToList();
                        var filteredLogData = new List<MetaAIResult>();
                        var now = System.DateTime.Now;

                        foreach (var item in logDataWithImages)
                        {
                            string trackId = item.TrackingObjectIndex;
                            System.Diagnostics.Debug.WriteLine($"Tracking ID :{trackId}");
                            if (!_aiLogCache.ContainsKey(trackId) || (now - _aiLogCache[trackId]) > _aiLogSuppressTime)
                            {
                                filteredLogData.Add(item);
                                _aiLogCache[trackId] = now;
                            }
                        }

                        if (filteredLogData.Count > 0)
                        {
                            SendMetaAIResult?.Invoke(this, filteredLogData);
                        }
                    }

                    // Dọn dẹp cache cũ để tránh rò rỉ bộ nhớ
                    if (_aiLogCache.Count > 100)
                    {
                        var oldKeys = _aiLogCache.Where(kv => (System.DateTime.Now - kv.Value) > _aiLogSuppressTime)
                                                 .Select(kv => kv.Key).ToList();
                        foreach (var key in oldKeys) _aiLogCache.Remove(key);
                    }
                        
                    
                    
                }
#endif
                // extracting gps
                if (frameInfo.Gps != null)
                {
                    GMap.NET.PointLatLng gps = new GMap.NET.PointLatLng(frameInfo.Gps.Latitude, frameInfo.Gps.Longitude);
                    SendGPS?.Invoke(this, gps);
                }
            }
            catch (Exception err)
            {
                Console.WriteLine("Error parsing SEI NAL payload. {0} ", err.Message);
            }
        }
        protected virtual string GetPipelineDescription()
        {
            LoggerManager.LogDebug($"Khởi tạo mô tả Pipeline (GStreamer). Chế độ: {(IsH264 ? "H264" : "H265")}");
            string h264 = @"rtspsrc protocols=tcp name=videoSource latency=2000 timeout=300000 do-retransmission=false videoSource. ! " +
         "queue leaky=1 name=video-queue ! watchdog timeout=300000 ! rtph264depay ! video/x-h264, stream-format=byte-stream, alignment=nal " +
         "! identity name=identity ! h264parse ! video/x-h264, stream-format=(string)avc, alignment=(string)au ! d3d11h264dec qos=false ! d3d11convert !  queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false qos=false " +
         "videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample  ! volume name=audioVolume ! wasapisink async=false sync=false";

            string h265 = "rtspsrc name=videoSource latency=2000 timeout=5000 videoSource. ! " +
                "queue leaky=1 name=video-queue ! watchdog timeout=15000 ! rtph265depay ! video/x-h265, stream-format=byte-stream, alignment=nal  " +
                "! identity name=identity ! h265parse ! video/x-h265, stream-format=(string)hvc1, alignment=(string)au ! d3d11h265dec ! d3d11convert ! queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false " +
                "videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample  ! volume name=audioVolume ! wasapisink async=false sync=false";
            string pipeline_description = this.IsH264 == true ? h264 : h265;
            return pipeline_description;
        }
        public void Send2Draw(List<MetaAIResult> result)
        {
            _aiResult.TryAdd(result, TimeSpan.FromMilliseconds(30));
        }


        public void Playing()
        {
            SetState(true);
        }
        public void Pause()
        {
            SetState(false);
        }
        private void SetState(bool play = true)
        {
            if (player == null) return;
            State state = play ? State.Playing : State.Paused;
            player.SetState(state);
        }

       // 

        public void QueryPositionPlaying()
        {
            _timer = new System.Threading.Timer((o) =>
            {
                if (player == null) return;
                bool ret = player.QueryPosition(Gst.Format.Time, out long pos);
                if (ret)
                {
                    double posSec = pos / (double)Gst.Constants.SECOND;
                    // Use InvariantCulture to ensure dot as decimal separator
                    PlayerSending?.Invoke(this, new PlayerInfo { Key=PlayerStatus.Position, Value = posSec.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) });
                }
            }, null, 0, 500);
        }

        public RtspPlayer(string rtsp, IntPtr window_handle, bool is_h264, bool is_nvidiagpu = false, int gpuSinkId = 0,
            
            Visibility showVideoSlider= Visibility.Collapsed)
        {

            try
            {
                CocoBlacklists=GlobalSystem.Instance.CocoBlacklists;

                AllowCocoClass = GlobalSystem.Instance.AllowCocoClass;
                this.IsH264 = is_h264;
                this.rtspAddres = rtsp;
                //this.rtspAddres = "rtsp://127.0.0.1:8554/playback/c46d9290-d4f6-49ad-8f56-8fa60d228c1f";
                this.windowHandle = window_handle;
                GpuSinkId = gpuSinkId;
                this.isNvidia_GPU = is_nvidiagpu;
                ShowVideoSlider = showVideoSlider;
                InitDraw();
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show("Error here : " + e.Message);
            }
        }

        // Hàm hỗ trợ tự động giải nén Embedded Resource ra thư mục Temp của Windows
        private string ExtractResourceToTempFile(string resourceName, string fallbackPath)
        {
            try
            {
                // Thay "V3SClient" bằng Default Namespace của bạn nếu khác
                string fullResourceName = "V3SClient.TLS." + resourceName; 
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(fullResourceName))
                {
                    if (stream == null) return fallbackPath; // Trả về fallback nếu chưa set Embedded Resource

                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), resourceName);
                    // Nếu file đã tồn tại và kích thước không đổi thì có thể bỏ qua bước ghi đè để tăng tốc
                    using (var fileStream = System.IO.File.Create(tempPath))
                    {
                        stream.CopyTo(fileStream);
                    }
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                libs.LoggerManager.LogWarn($"Không thể giải nén resource {resourceName}. Chạy fallback. Lỗi: {ex.Message}");
                return fallbackPath;
            }
        }

        protected virtual bool CreatePipeline()
        {
            // Tự động giải nén từ Embedded Resource ra thư mục Temp. Nếu không có sẽ fallback về file cục bộ.
            string rtspCaCert = ExtractResourceToTempFile("ca.pem", "TLS\\ca.pem");
            string rtspCertPem = ExtractResourceToTempFile("client_cert.pem", "TLS\\client_cert.pem");
            string rtspCertKey = ExtractResourceToTempFile("client.key", "TLS\\client.key");
            try
            {

            //"spliter. !  queue !  autovideosink ";

                string pipelineDescription = GetPipelineDescription();

                if (player != null)
                    this.Dispose();

                player = (Pipeline)Gst.Parse.Launch(pipelineDescription);
                videoSource = player.GetByName("videoSource");
                videoSource["location"] = this.rtspAddres;               
                videoSource["is-live"] = true;
#if TLS
                TlsCertificate caCertificate = new TlsCertificate(rtspCaCert);
                TlsCertificate cert = new TlsCertificate(rtspCertPem, rtspCertKey);

                RtspClientTlsInteraction interaction = new RtspClientTlsInteraction(cert, caCertificate);
                videoSource["tls-interaction"] = interaction;
                videoSource["tls-validation-flags"] = TlsCertificateFlags.GenericError;
                

#else
#endif

                videoQueue = player.GetByName("video-queue");
                audioQueue = player.GetByName("audio-queue");
                identity = player.GetByName("identity");            
               
                videoOverlay = player.GetByName("videoOverlay");
                audioVolume = player.GetByName("audioVolume");
                audioVolume["volume"] = 0;
                videoSource.PadAdded += (sender, args) =>
                {
                    Pad newPad = args.NewPad;
                    Caps caps = newPad.QueryCaps();

                    if (caps == null)
                    {
                        newPad.Dispose();
                        return;
                    }
                    string capsStr = caps.ToString();

                    if (capsStr.Contains("video"))
                    {
                        Pad videoPad = videoQueue.GetStaticPad("sink");
                        newPad.Link(videoPad);
                        videoPad.Dispose();
                    }
                    if (capsStr.Contains("audio"))
                    {
                        Pad audioPad = audioQueue.GetStaticPad("sink");
                        newPad.Link(audioPad);
                        audioPad.Dispose();
                    }
                    caps.Dispose();
                    newPad.Dispose();
                };
                return true;
            }
            catch (Exception err)
            {
                LoggerManager.LogException(err, "Lỗi khi tạo ứng dụng luồng camera (Create Pipeline).");
                return false;
            }
        }
        protected void Monitor(object bus, SyncMessageArgs sargs)
        {
            Gst.Message msg = sargs.Message;

            if (msg.Type == MessageType.Eos || msg.Type == MessageType.Error)
            {
                EOFnError(msg);
                msg.Dispose();
                sargs.Message.Dispose();
                return;
            }
            if (ShowVideoSlider== Visibility.Visible && msg.Src ==player && msg.Type == MessageType.StateChanged)
            {

                State oldState, newState, pendingState;
                msg.ParseStateChanged(out oldState, out newState, out pendingState);
                if (newState == State.Playing )
                {
                    bool ret = player.QueryDuration(Gst.Format.Time, out long duration);
                    if (ret)
                    {
                        duration = duration / Gst.Constants.SECOND;
                        PlayerSending?.Invoke(this, new PlayerInfo { Key = PlayerStatus.Duration, Value = duration.ToString() });
                    }
                }
               
                msg.Dispose();
                sargs.Message.Dispose();
                return;
            }
            // Video Overlay processing

            if (!Gst.Video.Global.IsVideoOverlayPrepareWindowHandleMessage(msg))
            {
                sargs.Message.Dispose();
                msg.Dispose();
                return;
            }

            Gst.Element src = msg.Src as Gst.Element;
            if (src == null)
                return;


            try
            {
                src["force-aspect-ratio"] = ForceAspectRatio;
            }
            catch (PropertyNotFoundException)
            {

            }


            Gst.Element overlay = null;
            Gst.Bin nsrc = (Gst.Bin)(this.player);
            if (nsrc == null)
                return;

            overlay = nsrc.GetByInterface(VideoOverlayAdapter.GType);
            if (overlay == null)
                return;

            VideoOverlayAdapter adapter = new VideoOverlayAdapter(overlay.Handle);

            adapter.WindowHandle = this.windowHandle;
            adapter.HandleEvents(true);



            sargs.Message.Dispose();
            msg.Dispose();
            src.Dispose();

        }

        // Live View keeps its existing fill behavior. Playback overrides this so
        // the original stream aspect ratio is preserved with letterboxing.
        protected virtual bool ForceAspectRatio
        {
            get { return false; }
        }

        protected void EOFnError(Gst.Message message)
        {
            switch (message.Type)
            {
                case MessageType.Error:                    
                    message.ParseError(out GLib.GException err, out string msg);                  
                    PlayerSending?.Invoke(this, new PlayerInfo { Key = PlayerStatus.Stop, Value ="Error: "+msg });
                    LoggerManager.LogError($"GStreamer Bus Error: {msg}");
                    break;
                case MessageType.Eos:
                    ReConnect();
                    break;
            }
            message.Dispose();
        }


        public virtual bool InitPipeline()
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

            this.videoOverlay.Connect("draw", Draw);

            Pad identity_src = identity.GetStaticPad("src");
            identity_src.AddProbe(Gst.PadProbeType.Buffer, GetFrameInfo);
            identity_src.Dispose();
            if (this.IsH264)
                this.SendSeiNal += ParseSeiNalH264;
            else
                this.SendSeiNal += ParseSeiNalH265;

            this.player.SetState(State.Playing);

           
            return ret;
        }

        protected virtual void ParseSeiNal_old(object sender, byte[] data)
        {
            if (data.Length < 5) return;

           
            // Check start-code prefix.
            if (!(data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x01))
                return;

            // NAL header and type.
            byte nalHeader = data[4];
            int nalUnitType = nalHeader & 0x1F;

            if (nalUnitType != 6) return;

            int ptr = 5;

            // Parse payload type.
            int payloadType = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadType += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;

            payloadType += data[ptr];
            ptr++;

            // Parse payload size.
            int payloadSize = 0;
            while (ptr < data.Length && data[ptr] == 0xFF)
            {
                payloadSize += 255;
                ptr++;
            }
            if (ptr >= data.Length) return;

            payloadSize += data[ptr];
            ptr++;

            // Verify there is enough data for the payload.
            if (ptr + payloadSize > data.Length) return;

            // Extract the payload.
            byte[] payload = new byte[payloadSize];
            System.Array.Copy(data, ptr, payload, 0, payloadSize);

            // Verify minimum payload length: 16-byte UUID + 4-byte text length.
            if (payload.Length < 20) return;

            // Extract UUID.
            byte[] uuidExtracted = new byte[16];
            System.Array.Copy(payload, 0, uuidExtracted, 0, 16);

            // Extract text length (4 bytes, little-endian).
            uint textLength = System.BitConverter.ToUInt32(payload, 16);

            int expectedPayloadLength = 16 + 4 + (int)textLength;
            if (payload.Length < expectedPayloadLength) return;

            // Extract text bytes and decode UTF-8 text.
            byte[] textBytes = new byte[textLength];
            System.Array.Copy(payload, 20, textBytes, 0, textLength);
            try
            {
                string text = Encoding.UTF8.GetString(textBytes);
                MetaFrame frameInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<MetaFrame>(text);
                if (frameInfo == null) return;
#if VIEW_ONLY
#else
                if (frameInfo.AiResults != null && frameInfo.AiResults.Count > 0)
                {
                    float scaleX = (float)widthFrame / frameInfo.ImageInfo.ImageWidth;
                    float scaleY = (float)heightFrame / frameInfo.ImageInfo.ImageHeight;

                    List<MetaAIResult> aiResults = frameInfo.AiResults.Where(x =>
                        (!CocoBlacklists.Contains(x.MetaType?.ToLower())))
                        .Select(x =>new MetaAIResult(
                            boundingBox: new SharpDX.RectangleF(x.Bbox.Left * scaleX, x.Bbox.Top * scaleY,
                                               x.Bbox.Width * scaleX, x.Bbox.Height * scaleY),
                            caption: x.Caption,
                            isBlackList: x.IsBlacklist,
                            isDisplay: true,
                            objectID: x.ObjectID,
                            eventType: x.EventType,
                            encodeObjectImage: x.EncodedObjectImage,
                            timeStamp: x.TimeStamp,
                            metaType: x.MetaType,
                            trackingObjectIndex: x.TrackingObjectIndex)).ToList();

                    List<MetaAIResult> logData = aiResults.Where(x =>( x.EventType == "appear" || x.IsBlackList == true)&& !AllowCocoClass.Contains(x.MetaType?.ToLower())).ToList();

                    if (logData != null && logData.Count > 0)// Send logData to UI
                        SendMetaAIResult?.Invoke(this, logData);

                    Send2Draw(aiResults); // Send aiResults to Draw

                }
#endif
                if (frameInfo.Gps != null)
                {
                    GMap.NET.PointLatLng gps = new GMap.NET.PointLatLng(frameInfo.Gps.Latitude, frameInfo.Gps.Longitude);
                    SendGPS?.Invoke(this, gps);
                }

            }
            catch (Exception err)
            {
                LoggerManager.LogException(err, "Lỗi khi parse SEI NAL payload");
            }
        }

        protected PadProbeReturn GetFrameInfo(Pad pad, PadProbeInfo info)
        {
            Gst.Buffer buffer = info.Buffer;
            if (buffer == null) return PadProbeReturn.Ok;

            MapInfo mapInfo;
            if (!buffer.Map(out mapInfo, Gst.MapFlags.Read))
                return PadProbeReturn.Ok;

            byte[] data = mapInfo.Data;

            SendSeiNal?.Invoke("SeiNalInfo", data);

            buffer.Unmap(mapInfo);
            buffer.Dispose();
            pad.Dispose();
           

            return PadProbeReturn.Ok;
        }

        public bool Speaker(bool status)
        {
            try
            {
                this.audioVolume["volume"] = status ? 1 : 0;
                return true;
            }
            catch
            {
                return false;
            }

        }

        public void Dispose()
        {

            ReleasePipeline();

            ReleaseDraw();
        }

       
        protected void CommandToSupper(PlayerStatus command)
        {
            switch (command)
            {
                case PlayerStatus.Stop:
                    PlayerSending?.Invoke(this, new PlayerInfo { Key = PlayerStatus.Stop, Value = "End of files" });                  
                    break;
            }
        }


        private void InitDraw()
        {
            _aiResult = new BlockingCollection<List<MetaAIResult>>(2);


            _redColorBrush = new RawColor4(1, 0, 0, 1);
            _greenColorBrush = new RawColor4(0, 1, 0, 1);
            _blueColorBrush = new RawColor4(0, 0, 1, 1);
            _roiInfoColorBrush = new RawColor4(0, 1, 0, 1);
            _goldOrangeColorBrush = new RawColor4(1, 0.65f, 0, 1);

            _solidFactory = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);

            _renderTargetProperties = new RenderTargetProperties(
              new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.R8G8B8A8_UNorm,
              SharpDX.Direct2D1.AlphaMode.Premultiplied));

            _textFactory = new SharpDX.DirectWrite.Factory();

            _textFormat = new TextFormat(_textFactory, "Arial", 24);
        }

        private void ReleaseDraw()
        {
            _textFormat?.Dispose();
            _textFactory?.Dispose();
            _solidColorBrush?.Dispose();
            _solidFactory?.Dispose();
        }

        protected void Draw(object o, SignalArgs args)
        {
            // Do not dispose args.Args[0] (the Gst Element) here as it will destroy the pipeline overlay!

            // Use non-blocking TryTake to avoid pausing the GStreamer rendering thread
            bool ret = _aiResult.TryTake(out List<MetaAIResult> arr, 0);
            bool drawSelectionDim = DimForCaptureSelection;

            if (!ret && !drawSelectionDim) return;

            if (arr == null) arr = new List<MetaAIResult>();
            if (arr.Count == 0 && !drawSelectionDim) return;
            try
            {
                arr = arr.ToList();
            }
            catch
            {
                LoggerManager.LogWarn("Không thể nhận dữ liệu AI để vẽ overlay (Draw Exception).");
                return;
            }

            var texturePointer = (IntPtr)args.Args[1];
            if (texturePointer == IntPtr.Zero) return;

            try
            {
                var renderTargetView = new RenderTargetView(texturePointer);

                using (var resource = renderTargetView.Resource)
                using (var surface = resource.QueryInterface<Surface>())
                {
                    var description = surface.Description;
                    if (widthFrame == 0)
                    {
                        widthFrame = description.Width;
                        heightFrame = description.Height;
                    }

                    using (var d2dRenderTarget = new RenderTarget(_solidFactory, surface, _renderTargetProperties))
                    {
                        bool shouldSendWarning = false;
                        bool isAbnormalyWarn = false;
                        string warningCaption = null;
                        d2dRenderTarget.BeginDraw();

                        // A native video sink is an HWND; a semi-transparent WPF
                        // overlay either disappears behind it or paints an opaque
                        // black rectangle. Drawing the cue here keeps every
                        // unselected tile 40% darker while the hovered tile stays
                        // at its original brightness.
                        if (drawSelectionDim)
                        {
                            using (var dimBrush = new SolidColorBrush(d2dRenderTarget, new RawColor4(0f, 0f, 0f, 0.40f)))
                            {
                                d2dRenderTarget.FillRectangle(new SharpDX.RectangleF(0, 0, description.Width, description.Height), dimBrush);
                            }
                        }

                        // Create the brush locally to avoid re-using a brush across different RenderTargets
                        using (var localSolidBrush = new SolidColorBrush(d2dRenderTarget, _blueColorBrush))
                        {
                            foreach (var item in arr)
                            {
                                bool isChange = false;
                                if (item.IsDisplay && item.Caption != null && item.Caption != "")
                                {
                                    // Đo kích thước thực tế của text caption
                                    using (var textLayout = new SharpDX.DirectWrite.TextLayout(_textFactory, item.Caption, _textFormat, 500, 50))
                                    {
                                        float textWidth = textLayout.Metrics.Width;
                                        float textHeight = textLayout.Metrics.Height;
                                        float padding = 3;

                                        float bgX = item.BoundingBox.Left;
                                        float bgY = item.BoundingBox.Top - textHeight - padding * 2;

                                        // Background vừa khít chữ
                                        var bgRect = new SharpDX.RectangleF(bgX, bgY, textWidth + padding * 2, textHeight + padding * 2);
                                        using (var bgBrush = new SolidColorBrush(d2dRenderTarget, new RawColor4(0, 0, 0, 0.6f)))
                                        {
                                            d2dRenderTarget.FillRectangle(bgRect, bgBrush);
                                        }

                                        // Vẽ text trên background
                                        var captionRect = new SharpDX.RectangleF(bgX + padding, bgY + padding, textWidth, textHeight);
                                        using (var captionBrush = new SolidColorBrush(d2dRenderTarget, _goldOrangeColorBrush))
                                        {
                                            d2dRenderTarget.DrawText(item.Caption, _textFormat, captionRect, captionBrush);
                                        }
                                    }
                                    isChange = true;
                                }
                               

                                if (RoiInfoShow && !string.IsNullOrEmpty(item.RoiDwellSecondsInfo))
                                {
                                    // Move text above the bounding box (Top - 30) instead of inside
                                    // Increased width to 120 to prevent the 's' from wrapping to the next line
                                    using (var roiInfoSolibrush=new SolidColorBrush(d2dRenderTarget, _greenColorBrush))
                                    {
                                        var timeRect = new SharpDX.RectangleF(item.BoundingBox.Left + 5, item.BoundingBox.Top + 2, 120, 35);
                                        d2dRenderTarget.DrawText(item.RoiDwellSecondsInfo, _textFormat, timeRect, roiInfoSolibrush);
                                        isChange = true;
                                    }
                                   
                                }
                                localSolidBrush.Color = item.IsBlackList ? _redColorBrush : isChange ? _goldOrangeColorBrush : _greenColorBrush;
                                d2dRenderTarget.DrawRectangle(item.BoundingBox, localSolidBrush, 3.0f);

                                if (item.IsBlackList && !string.IsNullOrWhiteSpace(item.Caption))
                                {
                                    if (!_warningCache.TryGetValue(item.Caption, out System.DateTime lastWarningTime) ||
                                        (System.DateTime.Now - lastWarningTime) > _warningSuppressTime)
                                    {
                                        if (item.MetaType == "anomaly") isAbnormalyWarn = true;
                                        shouldSendWarning = true;
                                        warningCaption = item.Caption;
                                        _warningCache[item.Caption] = System.DateTime.Now;
                                    }
                                }
                            }
                        }
                        d2dRenderTarget.EndDraw();
                        if (shouldSendWarning)
                        {
                            SendWarning?.Invoke(this, isAbnormalyWarn?"abnormal": "1");
                        }
                    }
                }
                
                // Set NativePointer to IntPtr.Zero before disposing the managed wrapper.
                // This prevents SharpDX from calling Release() on the COM object owned by GStreamer.
                renderTargetView.NativePointer = IntPtr.Zero;
                renderTargetView.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"â Œ Draw Exception: {ex.Message}");
            }
            return;
        }

        protected virtual void ReConnect()
        {
          
          PlayerSending?.Invoke(this, new PlayerInfo { Key = PlayerStatus.Eof, Value = "End of stream" });
          LoggerManager.LogDebug("Gửi trạng thái EOF để thực hiện kết nối lại luồng.");
        }

        public bool SetRate(float rate)
        {
            rate = System.Math.Max(0.1f, rate);
            rate = System.Math.Min(_maxRate, rate);
            currentRate = rate;
            if (player == null) return false;
            bool result = player.QueryPosition(Gst.Format.Time, out long position);
            if (!result) return false;
            player.Seek(currentRate, Gst.Format.Time, SeekFlags.Flush | SeekFlags.KeyUnit, Gst.SeekType.Set, position, Gst.SeekType.None, 0);
            return true;

        }
        public void SpeedDown()
        {
            float temp = this.currentRate;
            currentRate -= this.stepRate;
            currentRate = System.Math.Max(currentRate, 0.1f);          
            bool result = this.SetRate(currentRate);
            if (!result) currentRate = temp;

        }
        public void SpeedUp()
        {
            currentRate += this.stepRate;
            currentRate = System.Math.Min(currentRate, 2.0f);
            bool result = this.SetRate(currentRate); ;
            if (!result) currentRate -= this.stepRate;
        }
        private void Seek(long seekStep)
        {
            bool ret = player.QueryPosition(Gst.Format.Time, out long pos);
            if (!ret) return;
            long newPos = pos + seekStep * Gst.Constants.SECOND;
            newPos = System.Math.Max(0, newPos);

            player.Seek(currentRate, Gst.Format.Time, SeekFlags.Flush | SeekFlags.KeyUnit, Gst.SeekType.Set, newPos, Gst.SeekType.None, 0);
        }

        public void SeekAbsolute(long targetTime)
        {
            if (player == null) return;
            player.Seek(currentRate, Gst.Format.Time, SeekFlags.Flush | SeekFlags.KeyUnit, Gst.SeekType.Set, targetTime, Gst.SeekType.None, 0);
        }
        protected virtual void ReleasePipeline()
        {
            lock (_pipelineLock)
            {
                if (player != null)
                {
                    try
                    {
                        // 1. Detach VideoOverlay from UI window first to prevent D3D11 crash
                        if (videoOverlay != null)
                        {
                            try
                            {
                                VideoOverlayAdapter adapter = new VideoOverlayAdapter(videoOverlay.Handle);
                                adapter.WindowHandle = IntPtr.Zero;
                                adapter.HandleEvents(false);
                            }
                            catch { }
                        }

                        // 2. Unhook bus signals before stopping
                        player.Bus.DisableSyncMessageEmission();
                        player.Bus.SyncMessage -= Monitor;

                        if (_timer != null)
                        {
                            _timer.Dispose();
                            _timer = null;
                        }

                        // 3. Request state change to NULL
                        player.SetState(State.Null);
                        
                        // Wait for state change to reach NULL
                        player.GetState(out State current, out State pending, 500 * Gst.Constants.MSECOND);
                    }
                    catch (Exception e)
                    {
                        LoggerManager.LogException(e, "Lỗi khi dừng pipeline trong ReleasePipeline");
                    }

                    try
                    {
                        // Dispose all elements to free hardware decoders/resources
                        videoSource?.Dispose(); videoSource = null;
                        videoQueue?.Dispose(); videoQueue = null;
                        videoOverlay?.Dispose(); videoOverlay = null;
                        audioQueue?.Dispose(); audioQueue = null;
                        audioVolume?.Dispose(); audioVolume = null;
                        identity?.Dispose(); identity = null;

                        player.Dispose();
                        player = null;
                    }
                    catch (Exception e)
                    {
                        LoggerManager.LogException(e, "Lỗi rò rỉ bộ nhớ hoặc dispose elements trong ReleasePipeline");
                    }

                    windowHandle = IntPtr.Zero;
                }
            }
        }

        public void SeekForward()
        {
            Seek(_seekStep);
        }
        public void SeekBackward()
        {
            Seek(-_seekStep);
        }
        public void SeekBySeconds(long seconds)
        {
            Seek(seconds);
        }
        public void SeekTo(long position)
        {
            if (player != null)
            {
                player.Seek(currentRate, Gst.Format.Time, SeekFlags.Flush | SeekFlags.KeyUnit, Gst.SeekType.Set, position, Gst.SeekType.None, 0);
            }
        }
    }
}