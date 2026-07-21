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
    public enum WhepPlaybackErrorKind_v3 { None, MissingStream, Server, Connection, Decoder, NoVideoFrame, StreamEnded, Playback }

    public sealed class WhepPlaybackStateChangedEventArgs_v3 : EventArgs
    {
        public WhepPlaybackStateChangedEventArgs_v3(WhepPlaybackState_v3 state, string message = null,
            WhepPlaybackErrorKind_v3 errorKind = WhepPlaybackErrorKind_v3.None,
            string userMessage = null, bool isRetryable = false)
        {
            State = state;
            Message = message;
            ErrorKind = errorKind;
            UserMessage = userMessage;
            IsRetryable = isRetryable;
        }
        public WhepPlaybackState_v3 State { get; private set; }
        public string Message { get; private set; }
        public WhepPlaybackErrorKind_v3 ErrorKind { get; private set; }
        public string UserMessage { get; private set; }
        public bool IsRetryable { get; private set; }
    }

    public partial class WhepPlayer_v3 : System.Windows.Controls.UserControl, IDisposable
    {
        private sealed class RtspSource_v3
        {
            public string Url { get; set; }
            public bool IsH264 { get; set; }
        }

        private readonly System.Windows.Forms.Panel _videoPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        private readonly System.Windows.Forms.Label _cameraBadge = new System.Windows.Forms.Label();
        private CancellationTokenSource _cancellation;
        private Pipeline _pipeline;
        private Camera _camera;
        private CameraStreamInfo _selectedStream;
        private readonly SemaphoreSlim _connectionGate = new SemaphoreSlim(1, 1);
        private IntPtr _videoWindowHandle;
        private bool _useAlternateCodec;
        private bool _alternateCodecAttempted;

        public event EventHandler<WhepPlaybackStateChangedEventArgs_v3> PlaybackStateChanged;
        public event EventHandler VideoMouseEnter;
        public event EventHandler VideoMouseMove;
        public event EventHandler VideoMouseLeave;

        public WhepPlayer_v3()
        {
            InitializeComponent();
            VideoHost.Child = _videoPanel;
            _cameraBadge.AutoSize = true;
            _cameraBadge.BackColor = System.Drawing.Color.FromArgb(15, 45, 70);
            _cameraBadge.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            _cameraBadge.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            _cameraBadge.ForeColor = System.Drawing.Color.FromArgb(33, 197, 93);
            _cameraBadge.Padding = new System.Windows.Forms.Padding(6, 3, 6, 3);
            _cameraBadge.Location = new System.Drawing.Point(7, 7);
            _cameraBadge.Visible = false;
            _cameraBadge.TabStop = false;
            _videoPanel.Controls.Add(_cameraBadge);
            _cameraBadge.BringToFront();
            // The GStreamer sink is hosted by a native WinForms child HWND,
            // so WPF mouse routing cannot see hover inside the video area.
            _videoPanel.MouseEnter += (s, e) => VideoMouseEnter?.Invoke(this, EventArgs.Empty);
            _videoPanel.MouseMove += (s, e) => VideoMouseMove?.Invoke(this, EventArgs.Empty);
            _videoPanel.MouseLeave += (s, e) => VideoMouseLeave?.Invoke(this, EventArgs.Empty);
            Unloaded += (s, e) => Dispose();
        }

        public Camera Camera
        {
            get { return _camera; }
            set
            {
                if (ReferenceEquals(_camera, value)) return;
                _camera = value;
                _useAlternateCodec = false;
                _alternateCodecAttempted = false;
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

        public void SetVideoSurfaceVisible(bool visible)
        {
            VideoHost.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            _videoPanel.Visible = visible;
            // Do not infer badge visibility from the video surface. During a
            // reconnect GStreamer briefly makes the surface visible before
            // the tile state is updated; inferring here would resurrect the
            // previous camera ID for one frame. SetCameraBadge is the single
            // owner of the badge visibility.
            if (!visible) _cameraBadge.Visible = false;
        }

        /// <summary>
        /// Keeps the native pipeline alive without allowing either its video
        /// surface or its WPF status layer to appear.  This is used while a
        /// fullscreen main stream warms up behind the already-playing sub
        /// stream.
        /// </summary>
        public void SetPresentationVisible(bool visible)
        {
            SetVideoSurfaceVisible(visible);
            if (!visible)
                StatusPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Pauses/resumes a live pipeline without disposing its RTSP session
        /// or native window. The operation is deliberately off the UI thread
        /// because GStreamer state changes can wait on the source.
        /// </summary>
        public void SetPipelinePaused(bool paused)
        {
            var pipeline = _pipeline;
            if (pipeline == null) return;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!ReferenceEquals(pipeline, _pipeline)) return;
                    pipeline.SetState(paused ? State.Paused : State.Playing);
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "Live View _v3 could not " +
                        (paused ? "pause" : "resume") + " the background stream.");
                }
            });
        }

        public void SetCameraBadge(string cameraId, bool visible, bool connected, bool error)
        {
            // Camera IDs are rendered by LiveTile_v3's WPF badge. This
            // WinForms label was the legacy overlay painted inside the video
            // surface and could remain visible over stale frames.
            _cameraBadge.Text = string.IsNullOrWhiteSpace(cameraId) ? string.Empty : "●  " + cameraId;
            _cameraBadge.ForeColor = error
                ? System.Drawing.Color.FromArgb(255, 145, 145)
                : connected
                ? System.Drawing.Color.FromArgb(33, 197, 93)
                    : System.Drawing.Color.FromArgb(255, 193, 7);
            // The WPF Popup in LiveTile_v3 is the only visible badge. Keep
            // this legacy in-video label disabled in every state.
            _cameraBadge.Visible = false;
        }

        public System.Threading.Tasks.Task ReconnectAsync() { return ConnectAsync(); }

        public void Disconnect()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            // Stopping an RTSP source can wait for the source timeout. Keep
            // that native teardown off the dispatcher just like startup.
            var pipeline = System.Threading.Interlocked.Exchange(ref _pipeline, null);
            if (pipeline != null)
                _ = System.Threading.Tasks.Task.Run(() => DisposePipelineInstance(pipeline));
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
            DisposePipelineInstance(pipeline);
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
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
                // GStreamer state changes can wait on an RTSP handshake. Never
                // perform them on WPF's dispatcher thread; doing so freezes all
                // input and layout while a camera is slow or unreachable.
                await _connectionGate.WaitAsync(cancellation.Token).ConfigureAwait(true);
                try
                {
                    await System.Threading.Tasks.Task.Run(() => DisposePipeline(), cancellation.Token).ConfigureAwait(true);
                }
                finally
                {
                    _connectionGate.Release();
                }

                var selectedStream = _selectedStream;
                var source = GetRtspSource(selectedCamera, selectedStream);
                if (source == null || string.IsNullOrWhiteSpace(source.Url))
                {
                    PublishError(WhepPlaybackErrorKind_v3.MissingStream,
                        "Camera does not provide a direct RTSP URL.", false);
                    return;
                }
                if (_useAlternateCodec)
                {
                    source.IsH264 = !source.IsH264;
                    LoggerManager.LogWarn("Live View _v3 retrying camera " +
                        (selectedCamera.camID ?? selectedCamera.name ?? "unknown") +
                        " with the alternate " + (source.IsH264 ? "H.264" : "H.265") + " decoder.");
                }
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_cancellation, cancellation) ||
                    !ReferenceEquals(_camera, selectedCamera))
                    return;

                // Capture the HWND while on the dispatcher. The sync bus
                // callback runs on a GStreamer thread and must not call
                // Control.Invoke back into a dispatcher that may be busy.
                _videoWindowHandle = GetVideoWindowHandle();
                await _connectionGate.WaitAsync(cancellation.Token).ConfigureAwait(true);
                try
                {
                    await System.Threading.Tasks.Task.Run(() => CreatePipeline(
                        source.Url,
                        source.IsH264,
                        _videoWindowHandle), cancellation.Token).ConfigureAwait(true);
                }
                finally
                {
                    _connectionGate.Release();
                }
                LoggerManager.LogInfo("Live View _v3 started direct GStreamer RTSP for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown") +
                    " (stream " + (selectedStream == null ? "fallback" : selectedStream.StreamType ?? "unknown") + "): " + source.Url);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_cancellation, cancellation)) return;
                LoggerManager.LogException(ex, "Live View _v3 direct RTSP connection failed for camera " +
                    (selectedCamera.camID ?? selectedCamera.name ?? "unknown"));
                var kind = ClassifyError(ex.Message);
                PublishError(kind, ex.Message, IsRetryableError(kind));
            }
        }

        private void CreatePipeline(string rtspUrl, bool isH264, IntPtr videoWindowHandle)
        {
            _videoWindowHandle = videoWindowHandle;
            // Keep the same Direct3D11 decode and render path as the original
            // V3 client.  This avoids software decode and the CPU-side
            // videoconvert copy once several cameras are open.  The URL and
            // codec are resolved together above so that the parser and decoder
            // always match the actual selected RTSP stream.
            var videoChain = isH264
                ? "rtph264depay ! h264parse ! video/x-h264,stream-format=(string)avc,alignment=(string)au ! d3d11h264dec qos=false"
                : "rtph265depay ! h265parse ! video/x-h265,stream-format=(string)hvc1,alignment=(string)au ! d3d11h265dec";
            var pipelineText =
                "rtspsrc name=videoSource protocols=tcp latency=300 timeout=15000000 drop-on-latency=true " +
                "videoSource. ! queue leaky=downstream max-size-buffers=8 ! application/x-rtp,media=video ! " +
                videoChain + " ! d3d11convert ! queue leaky=downstream max-size-buffers=4 ! " +
                "d3d11overlay name=videoOverlay ! d3d11videosink async=false sync=false qos=false";
            _pipeline = (Pipeline)Parse.Launch(pipelineText);
            var source = _pipeline.GetByName("videoSource");
            source["location"] = rtspUrl;
            _pipeline.Bus.EnableSyncMessageEmission();
            _pipeline.Bus.SyncMessage += OnSyncMessage;
            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("GStreamer could not start the direct RTSP playback pipeline.");
        }

        private static RtspSource_v3 GetRtspSource(Camera camera, CameraStreamInfo selectedStream)
        {
            if (camera == null) return null;
            var isAi = selectedStream != null && selectedStream.IsAiMode == true;
            var isMain = selectedStream != null && string.Equals(selectedStream.StreamType, "main", StringComparison.OrdinalIgnoreCase);
            var candidate = isAi
                ? (isMain ? camera.RtspUrlMainAI : camera.RtspUrlAI)
                : (isMain ? camera.RtspUrlMainRaw : camera.RtspUrlRaw);
            var isH264 = isAi
                ? (isMain ? camera.IsH264MainAI : camera.IsH264AI)
                : (isMain ? camera.IsH264MainRaw : camera.IsH264Raw);
            if (!string.IsNullOrWhiteSpace(candidate) && System.Uri.IsWellFormedUriString(candidate, System.UriKind.Absolute))
                return new RtspSource_v3 { Url = candidate, IsH264 = isH264 };

            candidate = selectedStream == null ? null : selectedStream.RtspRelayRaw;
            if (!string.IsNullOrWhiteSpace(candidate) && System.Uri.IsWellFormedUriString(candidate, System.UriKind.Absolute))
                return new RtspSource_v3 { Url = candidate, IsH264 = IsH264(camera, selectedStream) };
            return IsDirectRtspUrl(camera.rtps)
                ? new RtspSource_v3 { Url = camera.rtps, IsH264 = camera.is_H264 }
                : null;
        }

        private static bool IsDirectRtspUrl(string value)
        {
            System.Uri uri;
            if (string.IsNullOrWhiteSpace(value) || !System.Uri.TryCreate(value, UriKind.Absolute, out uri))
                return false;
            if (!string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase))
                return false;
            return !string.IsNullOrWhiteSpace(uri.Host) &&
                   uri.Host.IndexOf("mediamtx", StringComparison.OrdinalIgnoreCase) < 0;
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
                    var kind = ClassifyError(errorMessage);
                    if (IsCodecNegotiationError(errorMessage) && !_alternateCodecAttempted)
                    {
                        // API metadata can be stale or a camera can change
                        // its encoder profile. Retry once with the paired
                        // H.264/H.265 pipeline before declaring failure.
                        _alternateCodecAttempted = true;
                        _useAlternateCodec = true;
                    }
                    PublishError(kind, errorMessage, IsRetryableError(kind));
                }));
                return;
            }
            if (message.Type == MessageType.Eos)
            {
                LoggerManager.LogWarn("Live View _v3 stream ended for camera " + (_camera == null ? "unknown" : _camera.camID));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PublishError(WhepPlaybackErrorKind_v3.StreamEnded, "The camera stream ended.", false);
                }));
                return;
            }
            if (!Gst.Video.Global.IsVideoOverlayPrepareWindowHandleMessage(message)) return;
            var overlay = _pipeline == null ? null : _pipeline.GetByInterface(VideoOverlayAdapter.GType);
            if (overlay == null) return;
            var adapter = new VideoOverlayAdapter(overlay.Handle);
            // This callback is raised by GStreamer, not WPF. Using the
            // handle captured before pipeline creation avoids a cross-thread
            // Control.Invoke deadlock when SetState is in progress.
            adapter.WindowHandle = _videoWindowHandle;
            adapter.HandleEvents(true);
            overlay.Dispose();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusPanel.Visibility = Visibility.Collapsed;
                _useAlternateCodec = false;
                _alternateCodecAttempted = false;
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

        private static WhepPlaybackErrorKind_v3 ClassifyError(string message)
        {
            var value = (message ?? string.Empty).ToLowerInvariant();
            if (value.Contains("404") || value.Contains("not found") || value.Contains("unauthorized") ||
                value.Contains("forbidden") || value.Contains("server") || value.Contains("no such stream"))
                return WhepPlaybackErrorKind_v3.Server;
            if (value.Contains("connection refused") || value.Contains("could not connect") ||
                value.Contains("network") || value.Contains("host unreachable") || value.Contains("no route") ||
                value.Contains("timed out") || value.Contains("timeout"))
                return WhepPlaybackErrorKind_v3.Connection;
            if (value.Contains("not-negotiated") || value.Contains("decode") || value.Contains("decoder") ||
                value.Contains("h264") || value.Contains("h265") || value.Contains("hevc") ||
                value.Contains("corrupt") || value.Contains("not-linked"))
                return WhepPlaybackErrorKind_v3.Decoder;
            return WhepPlaybackErrorKind_v3.Playback;
        }

        private static bool IsCodecNegotiationError(string message)
        {
            var value = (message ?? string.Empty).ToLowerInvariant();
            return value.Contains("not-negotiated") || value.Contains("not-linked");
        }

        private static bool IsRetryableError(WhepPlaybackErrorKind_v3 kind)
        {
            return kind == WhepPlaybackErrorKind_v3.Connection || kind == WhepPlaybackErrorKind_v3.Decoder;
        }

        private static string GetUserMessage(WhepPlaybackErrorKind_v3 kind)
        {
            switch (kind)
            {
                case WhepPlaybackErrorKind_v3.MissingStream:
                    return "Máy chủ chưa trả về đường dẫn phát trực tiếp cho camera này.";
                case WhepPlaybackErrorKind_v3.Server:
                    return "Máy chủ phát trực tiếp không trả được luồng camera. Kiểm tra dịch vụ hoặc cấu hình camera.";
                case WhepPlaybackErrorKind_v3.Connection:
                    return "Không thể kết nối đến camera. Kiểm tra mạng, nguồn camera hoặc đường truyền RTSP.";
                case WhepPlaybackErrorKind_v3.Decoder:
                    return "Không thể giải mã luồng video. Ứng dụng sẽ thử kết nối lại bằng luồng camera hiện có.";
                case WhepPlaybackErrorKind_v3.NoVideoFrame:
                    return "Đã kết nối nhưng không nhận được khung hình video từ camera.";
                case WhepPlaybackErrorKind_v3.StreamEnded:
                    return "Luồng camera đã kết thúc. Vui lòng kiểm tra trạng thái camera.";
                default:
                    return "Không thể phát luồng camera. Kiểm tra cấu hình và thử lại.";
            }
        }

        private void PublishError(WhepPlaybackErrorKind_v3 kind, string technicalMessage, bool isRetryable)
        {
            StatusText.Text = GetUserMessage(kind);
            StatusPanel.Visibility = Visibility.Visible;
            RaiseState(WhepPlaybackState_v3.Error, technicalMessage, kind, GetUserMessage(kind), isRetryable);
        }

        private void RaiseState(WhepPlaybackState_v3 state, string message = null,
            WhepPlaybackErrorKind_v3 errorKind = WhepPlaybackErrorKind_v3.None,
            string userMessage = null, bool isRetryable = false)
        {
            PlaybackStateChanged?.Invoke(this, new WhepPlaybackStateChangedEventArgs_v3(state, message, errorKind, userMessage, isRetryable));
        }

        private void DisposePipeline()
        {
            var pipeline = System.Threading.Interlocked.Exchange(ref _pipeline, null);
            DisposePipelineInstance(pipeline);
        }

        private void DisposePipelineInstance(Pipeline pipeline)
        {
            if (pipeline == null) return;
            try
            {
                pipeline.Bus.SyncMessage -= OnSyncMessage;
                pipeline.SetState(State.Null);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Live View _v3 GStreamer pipeline cleanup failed");
            }
            finally
            {
                try { pipeline.Dispose(); }
                catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 GStreamer pipeline dispose failed"); }
            }
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
