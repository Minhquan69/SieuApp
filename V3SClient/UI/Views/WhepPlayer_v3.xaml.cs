using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Gst;
using Gst.Video;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.UI.Views
{
    public enum WhepPlaybackState_v3 { Connecting, Playing, Stopped, Error }

    public sealed class WhepPlaybackStateChangedEventArgs_v3 : EventArgs
    {
        public WhepPlaybackStateChangedEventArgs_v3(WhepPlaybackState_v3 state, string message = null) { State = state; Message = message; }
        public WhepPlaybackState_v3 State { get; private set; }
        public string Message { get; private set; }
    }

    public partial class WhepPlayer_v3 : System.Windows.Controls.UserControl, IDisposable
    {
        private readonly System.Windows.Forms.Panel _videoPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        private CancellationTokenSource _cancellation;
        private Pipeline _pipeline;
        private Camera _camera;
        private CameraStreamInfo _selectedStream;

        public event EventHandler<WhepPlaybackStateChangedEventArgs_v3> PlaybackStateChanged;

        public WhepPlayer_v3()
        {
            InitializeComponent();
            VideoHost.Child = _videoPanel;
            Loaded += async (s, e) =>
            {
                if (_pipeline == null && _cancellation == null)
                    await ConnectAsync();
            };
            Unloaded += (s, e) => Dispose();
        }

        public Camera Camera
        {
            get { return _camera; }
            set
            {
                if (ReferenceEquals(_camera, value)) return;
                _camera = value;
                if (IsLoaded) _ = ConnectAsync();
            }
        }

        public CameraStreamInfo SelectedStream
        {
            get { return _selectedStream; }
            set
            {
                if (ReferenceEquals(_selectedStream, value)) return;
                _selectedStream = value;
            }
        }

        public System.Threading.Tasks.Task ReconnectAsync() { return ConnectAsync(); }

        public void Disconnect()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            DisposePipeline();
            StatusText.Text = _camera == null ? "Select a camera to connect." : "Camera disconnected.";
            StatusPanel.Visibility = Visibility.Visible;
            RaiseState(WhepPlaybackState_v3.Stopped);
        }

        public void SetDisconnectedStatus()
        {
            StatusText.Text = _camera == null ? "Select a camera to connect." : "Camera disconnected.";
            StatusPanel.Visibility = Visibility.Visible;
            RaiseState(WhepPlaybackState_v3.Stopped);
        }

        public void RequestDisconnect()
        {
            _cancellation?.Cancel();
        }

        /// <summary>
        /// Matches the original V3 live view: native GStreamer disposal is run
        /// concurrently for each camera. No WPF controls are touched here.
        /// </summary>
        public void DisposePipelineInBackground()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            var pipeline = System.Threading.Interlocked.Exchange(ref _pipeline, null);
            if (pipeline == null) return;
            pipeline.Bus.SyncMessage -= OnSyncMessage;
            pipeline.SetState(State.Null);
            pipeline.Dispose();
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            DisposePipeline();
            if (_camera == null) return;

            _cancellation?.Cancel();
            _cancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var selectedCamera = _camera;

            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Connecting to " + (selectedCamera.name ?? selectedCamera.camID) + "...";
            RaiseState(WhepPlaybackState_v3.Connecting);
            try
            {
                var selectedStream = _selectedStream;
                var rtspUrl = GetRtspUrl(selectedCamera, selectedStream);
                if (string.IsNullOrWhiteSpace(rtspUrl))
                    throw new InvalidOperationException("Camera does not provide a direct RTSP relay URL.");
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_cancellation, cancellation) ||
                    !ReferenceEquals(_camera, selectedCamera))
                    return;

                CreatePipeline(rtspUrl, IsH264(selectedCamera, selectedStream));
                LoggerManager.LogInfo("Live View _v3 started direct GStreamer RTSP for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown") + ": " + rtspUrl);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_cancellation, cancellation)) return;
                LoggerManager.LogException(ex, "Live View _v3 direct RTSP connection failed for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown"));
                StatusText.Text = "Unable to connect to this camera through the live stream relay.";
                StatusPanel.Visibility = Visibility.Visible;
                RaiseState(WhepPlaybackState_v3.Error, ex.Message);
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
                videoChain + " ! d3d11convert ! d3d11overlay name=videoOverlay ! " +
                "d3d11videosink async=false sync=false qos=false";
            _pipeline = (Pipeline)Parse.Launch(pipelineText);
            var source = _pipeline.GetByName("videoSource");
            source["location"] = rtspUrl;
            _pipeline.Bus.EnableSyncMessageEmission();
            _pipeline.Bus.SyncMessage += OnSyncMessage;
            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("GStreamer could not start the WHEP playback pipeline.");
        }

        private static string GetRtspUrl(Camera camera, CameraStreamInfo selectedStream)
        {
            if (camera == null) return null;
            var isAi = selectedStream != null && selectedStream.IsAiMode == true;
            var candidate = isAi
                ? (camera.RtspUrlAI ?? camera.RtspUrlMainAI)
                : (camera.RtspUrlRaw ?? camera.RtspUrlMainRaw);
            if (!string.IsNullOrWhiteSpace(candidate) && System.Uri.IsWellFormedUriString(candidate, System.UriKind.Absolute))
                return candidate;

            candidate = selectedStream == null ? null : selectedStream.RtspRelayRaw;
            if (!string.IsNullOrWhiteSpace(candidate) && System.Uri.IsWellFormedUriString(candidate, System.UriKind.Absolute))
                return candidate;
            return camera.rtps;
        }

        private void OnSyncMessage(object sender, SyncMessageArgs args)
        {
            var message = args.Message;
            if (message.Type == MessageType.Error)
            {
                message.ParseError(out GLib.GException error, out string details);
                var errorMessage = string.IsNullOrWhiteSpace(details) ? error.Message : details;
                LoggerManager.LogError("Live View _v3 GStreamer error: " + errorMessage, error);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = "Video playback failed. Check the camera and live stream relay.";
                    StatusPanel.Visibility = Visibility.Visible;
                    RaiseState(WhepPlaybackState_v3.Error, errorMessage);
                }));
                return;
            }
            if (message.Type == MessageType.Eos)
            {
                LoggerManager.LogWarn("Live View _v3 stream ended for camera " + (_camera == null ? "unknown" : _camera.camID));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = "The camera stream ended.";
                    StatusPanel.Visibility = Visibility.Visible;
                    RaiseState(WhepPlaybackState_v3.Error, "The camera stream ended.");
                }));
                return;
            }
            if (!Gst.Video.Global.IsVideoOverlayPrepareWindowHandleMessage(message)) return;
            var overlay = _pipeline == null ? null : _pipeline.GetByInterface(VideoOverlayAdapter.GType);
            if (overlay == null) return;
            var adapter = new VideoOverlayAdapter(overlay.Handle);
                adapter.WindowHandle = GetVideoWindowHandle();
            adapter.HandleEvents(true);
            overlay.Dispose();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusPanel.Visibility = Visibility.Collapsed;
                RaiseState(WhepPlaybackState_v3.Playing);
            }));
        }

        private static bool IsH264(Camera camera, CameraStreamInfo selectedStream)
        {
            var stream = selectedStream ?? (camera.Streams == null
                ? null
                : camera.Streams.FirstOrDefault(item => item != null &&
                    string.Equals(item.StreamType, "main", StringComparison.OrdinalIgnoreCase)) ?? camera.Streams.FirstOrDefault());
            var codec = stream == null ? null : stream.Codec;
            if (!string.IsNullOrWhiteSpace(codec))
            {
                if (codec.IndexOf("265", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    codec.IndexOf("hevc", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (codec.IndexOf("264", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    codec.IndexOf("avc", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return camera.is_H264;
        }

        private void RaiseState(WhepPlaybackState_v3 state, string message = null)
        {
            PlaybackStateChanged?.Invoke(this, new WhepPlaybackStateChangedEventArgs_v3(state, message));
        }

        private void DisposePipeline()
        {
            if (_pipeline == null) return;
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
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            DisposePipeline();
        }
    }
}
