using System;
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
            var videoChain = isH264
                ? "rtph264depay ! h264parse ! d3d11h264dec qos=false"
                : "rtph265depay ! h265parse ! d3d11h265dec";
            var pipelineText =
                "rtspsrc name=videoSource protocols=tcp latency=300 timeout=15000000 do-retransmission=false " +
                "videoSource. ! queue leaky=downstream max-size-buffers=8 ! application/x-rtp,media=video ! " +
                videoChain +
                " ! d3d11convert ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false qos=false";

            _pipeline = (Pipeline)Parse.Launch(pipelineText);
            var source = _pipeline.GetByName("videoSource");
            source["location"] = rtspUrl;
            _pipeline.Bus.EnableSyncMessageEmission();
            _pipeline.Bus.SyncMessage += OnSyncMessage;

            var result = _pipeline.SetState(State.Playing);
            if (result == StateChangeReturn.Failure)
                throw new InvalidOperationException("GStreamer could not start the RTSP pipeline.");
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
            if (_pipeline == null)
                return;

            _pipeline.Bus.SyncMessage -= OnSyncMessage;
            _pipeline.SetState(State.Null);
            _pipeline.Dispose();
            _pipeline = null;
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
