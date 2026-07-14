using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Gst;
using Gst.Video;
using V3SClient.Services;
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
        private readonly LiveStreamService_v3 _streamService = new LiveStreamService_v3();
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

        public void RequestDisconnect()
        {
            _cancellation?.Cancel();
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
                var connection = await _streamService.ConnectAsync(selectedCamera, selectedStream, cancellation.Token);
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_cancellation, cancellation) ||
                    !ReferenceEquals(_camera, selectedCamera))
                    return;

                CreatePipeline(connection.PlaybackUrl, IsH264(selectedCamera, selectedStream));
                LoggerManager.LogInfo("Live View _v3 started GStreamer WHEP for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown") + ": " + connection.PlaybackUrl);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_cancellation, cancellation)) return;
                LoggerManager.LogException(ex, "Live View _v3 WHEP connection failed for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown"));
                StatusText.Text = "Unable to connect to this camera through the live stream relay.";
                StatusPanel.Visibility = Visibility.Visible;
                RaiseState(WhepPlaybackState_v3.Error, ex.Message);
            }
        }

        private void CreatePipeline(string whepUrl, bool isH264)
        {
            var depay = isH264 ? "rtph264depay ! h264parse ! d3d11h264dec" : "rtph265depay ! h265parse ! d3d11h265dec";
            // The relay cameras can carry G.711 audio even though this control renders
            // video only. whepsrc defaults to OPUS-only audio, and MediaMTX rejects the
            // complete WHEP offer when the camera's PCMU/PCMA track has no common codec.
            const string audioCaps =
                "application/x-rtp,media=(string)audio,encoding-name=(string)PCMU,payload=(int)0,clock-rate=(int)8000;" +
                "application/x-rtp,media=(string)audio,encoding-name=(string)PCMA,payload=(int)8,clock-rate=(int)8000;" +
                "application/x-rtp,media=(string)audio,encoding-name=(string)OPUS,payload=(int)96,clock-rate=(int)48000";
            var pipelineText = "whepsrc name=videoSource timeout=15 use-link-headers=true audio-caps=\"" + audioCaps + "\" ! queue ! " + depay +
                " ! d3d11convert ! d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false";
            _pipeline = (Pipeline)Parse.Launch(pipelineText);
            var source = _pipeline.GetByName("videoSource");
            source["whep-endpoint"] = whepUrl;
            _pipeline.Bus.EnableSyncMessageEmission();
            _pipeline.Bus.SyncMessage += OnSyncMessage;
            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("GStreamer could not start the WHEP playback pipeline.");
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
