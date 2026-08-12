using System;
using System.Threading;
using System.Windows;
using Gst;
using Gst.Video;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.UI.Views
{
    public partial class GstRtspPlayer_v3 : System.Windows.Controls.UserControl, IDisposable
    {
        private readonly System.Windows.Forms.Panel _videoPanel =
            new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        private Pipeline _pipeline;
        private GstBusMessagePump _busMessagePump;
        private Camera _camera;

        public GstRtspPlayer_v3()
        {
            InitializeComponent();
            VideoHost.Child = _videoPanel;
            Loaded += (s, e) =>
            {
                if (_pipeline == null)
                    Connect();
            };
            Unloaded += (s, e) => Dispose();
        }

        public Camera Camera
        {
            get { return _camera; }
            set
            {
                if (ReferenceEquals(_camera, value))
                    return;

                _camera = value;
                if (IsLoaded)
                    Connect();
            }
        }

        private void Connect()
        {
            DisposePipeline();
            if (_camera == null)
                return;

            var rtspUrl = GetRtspUrl(_camera);
            if (string.IsNullOrWhiteSpace(rtspUrl))
            {
                ShowStatus("This camera does not provide an RTSP relay URL.");
                return;
            }

            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Connecting to " + (_camera.name ?? _camera.camID) + "...";

            try
            {
                var isH264 = GetIsH264(_camera, rtspUrl);
                CreatePipeline(rtspUrl, isH264);
                LoggerManager.LogInfo("Live View _v3 started direct GStreamer RTSP for camera " +
                                      (_camera.camID ?? _camera.name ?? "unknown") +
                                      " using " + (isH264 ? "H264" : "H265"));
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Live View _v3 direct GStreamer RTSP failed for camera " +
                                                (_camera.camID ?? _camera.name ?? "unknown"));
                ShowStatus("Unable to open this camera by RTSP. Check the relay endpoint and network access.");
            }
        }

        private void CreatePipeline(string rtspUrl, bool isH264)
        {
            // Same production-tested RTSP topology used by models.RtspPlayer
            // and the legacy client. Keep its queue order/timeouts identical
            // so floating/secondary live surfaces behave like the main wall.
            var pipelineText = isH264
                ? "rtspsrc protocols=tcp name=videoSource latency=2000 timeout=300000 do-retransmission=false videoSource. ! " +
                  "queue leaky=1 name=video-queue ! watchdog timeout=300000 ! rtph264depay ! video/x-h264, stream-format=byte-stream, alignment=nal " +
                  "! identity name=identity ! h264parse ! video/x-h264, stream-format=(string)avc, alignment=(string)au ! d3d11h264dec qos=false ! d3d11convert ! queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false qos=false " +
                  "videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample ! volume name=audioVolume ! wasapisink async=false sync=false"
                : "rtspsrc name=videoSource latency=2000 timeout=5000 videoSource. ! " +
                  "queue leaky=1 name=video-queue ! watchdog timeout=15000 ! rtph265depay ! video/x-h265, stream-format=byte-stream, alignment=nal " +
                  "! identity name=identity ! h265parse ! video/x-h265, stream-format=(string)hvc1, alignment=(string)au ! d3d11h265dec ! d3d11convert ! queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false qos=false " +
                  "videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample ! volume name=audioVolume ! wasapisink async=false sync=false";

            try
            {
                _pipeline = (Pipeline)Parse.Launch(pipelineText);
                var source = _pipeline.GetByName("videoSource");
                source["location"] = rtspUrl;
                var messagePump = new GstBusMessagePump(_pipeline.Bus);
                _busMessagePump = messagePump;
                messagePump.Bus.EnableSyncMessageEmission();
                messagePump.Bus.SyncMessage += OnSyncMessage;

                var result = _pipeline.SetState(State.Playing);
                if (result == StateChangeReturn.Failure)
                    throw new InvalidOperationException("GStreamer could not start the RTSP pipeline.");
            }
            catch
            {
                DisposePipeline();
                throw;
            }
        }

        private void OnSyncMessage(object sender, SyncMessageArgs args)
        {
            var message = args.Message;
            if (message.Type == MessageType.Error)
            {
                message.ParseError(out GLib.GException error, out string details);
                var technicalMessage = string.IsNullOrWhiteSpace(details) ? error.Message : details;
                LoggerManager.LogError("Live View _v3 direct RTSP GStreamer error: " + technicalMessage, error);
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowStatus("Video playback failed. Check the camera RTSP relay and codec.")));
                return;
            }

            if (message.Type == MessageType.Eos)
            {
                LoggerManager.LogWarn("Live View _v3 direct RTSP stream ended for camera " +
                                      (_camera == null ? "unknown" : _camera.camID));
                Dispatcher.BeginInvoke(new Action(() => ShowStatus("The camera stream ended.")));
                return;
            }

            if (!Gst.Video.Global.IsVideoOverlayPrepareWindowHandleMessage(message))
                return;

            var overlay = _pipeline == null ? null : _pipeline.GetByInterface(VideoOverlayAdapter.GType);
            if (overlay == null)
                return;

            var adapter = new VideoOverlayAdapter(overlay.Handle);
            adapter.WindowHandle = GetVideoWindowHandle();
            adapter.HandleEvents(true);
            overlay.Dispose();
            Dispatcher.BeginInvoke(new Action(() => StatusPanel.Visibility = Visibility.Collapsed));
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusPanel.Visibility = Visibility.Visible;
        }

        private static string GetRtspUrl(Camera camera)
        {
            return camera.RtspUrlRaw ?? camera.RtspUrlMainRaw ?? camera.rtps;
        }

        private static bool GetIsH264(Camera camera, string selectedUrl)
        {
            if (string.Equals(selectedUrl, camera.RtspUrlRaw, StringComparison.OrdinalIgnoreCase))
                return camera.IsH264Raw;
            if (string.Equals(selectedUrl, camera.RtspUrlMainRaw, StringComparison.OrdinalIgnoreCase))
                return camera.IsH264MainRaw;
            return camera.is_H264;
        }

        private void DisposePipeline()
        {
            var pipeline = _pipeline;
            _pipeline = null;
            var messagePump = Interlocked.Exchange(ref _busMessagePump, null);
            if (pipeline == null)
            {
                if (messagePump != null) messagePump.Dispose();
                return;
            }

            if (messagePump != null)
            {
                try { messagePump.Bus.SyncMessage -= OnSyncMessage; } catch { }
                try { messagePump.Bus.DisableSyncMessageEmission(); } catch { }
                try { messagePump.Dispose(); }
                catch (Exception ex) { LoggerManager.LogException(ex, "Could not stop direct RTSP bus message pump"); }
            }
            try { pipeline.SetState(State.Null); }
            finally { pipeline.Dispose(); }
        }

        private IntPtr GetVideoWindowHandle()
        {
            if (_videoPanel.IsDisposed) return IntPtr.Zero;
            if (!_videoPanel.InvokeRequired) return _videoPanel.Handle;
            return (IntPtr)_videoPanel.Invoke(new Func<IntPtr>(() => _videoPanel.Handle));
        }

        public void Dispose()
        {
            DisposePipeline();
        }
    }
}
