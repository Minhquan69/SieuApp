using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Gst;
using Gst.Video;
using Newtonsoft.Json;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.Services;
using Format = SharpDX.DXGI.Format;
using SignalArgs = GLib.SignalArgs;

namespace V3SClient.UI.Views
{
    public enum WhepPlaybackState_v3 { Connecting, Playing, Stopped, Error }
    public enum WhepPlaybackErrorKind_v3 { None, MissingStream, Server, Connection, Decoder, NoVideoFrame, StreamEnded, Playback }

    /// <summary>
    /// One-layer native counterpart of the WPF camera-ID badge.  Drawing the
    /// icon and title in one control avoids child-control z-order/vertical
    /// alignment artefacts above the D3D sink.
    /// </summary>
    internal sealed class CameraIdBadgeControl_v3 : System.Windows.Forms.Control
    {
        private const int Radius = 3;
        private string _cameraId = string.Empty;
        private System.Drawing.Color _statusColor = System.Drawing.Color.FromArgb(100, 116, 139);
        private bool _audioEnabled;

        public CameraIdBadgeControl_v3()
        {
            DoubleBuffered = true;
            BackColor = System.Drawing.Color.FromArgb(21, 47, 72); // VmsElevated
            Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            Height = 24;
            TabStop = false;
        }

        public void SetBadge(string cameraId, System.Drawing.Color statusColor, bool audioEnabled)
        {
            _cameraId = cameraId ?? string.Empty;
            _statusColor = statusColor;
            _audioEnabled = audioEnabled;
            var measured = System.Windows.Forms.TextRenderer.MeasureText(_cameraId, Font,
                new System.Drawing.Size(int.MaxValue, Height),
                System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine);
            var audioWidth = 0;
            if (audioEnabled)
            {
                using (var audioFont = new System.Drawing.Font("Segoe MDL2 Assets", 10F,
                    System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel))
                {
                    audioWidth = System.Windows.Forms.TextRenderer.MeasureText("\uE995", audioFont,
                        new System.Drawing.Size(int.MaxValue, Height),
                        System.Windows.Forms.TextFormatFlags.NoPadding |
                        System.Windows.Forms.TextFormatFlags.SingleLine).Width;
                }
            }
            Width = Math.Max(48, 28 + measured.Width + (audioEnabled ? audioWidth + 2 : 0) + 5);
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (var path = CreateRoundedPath(new System.Drawing.Rectangle(0, 0, Width - 1, Height - 1), Radius))
                Region = new System.Drawing.Region(path);
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            using (var path = CreateRoundedPath(new System.Drawing.Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var background = new System.Drawing.SolidBrush(BackColor))
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(42, 77, 112))) // VmsBorderStrong
            using (var iconPen = new System.Drawing.Pen(_statusColor, 1.35F))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(pen, path);

                // Compact camera outline, centred on the badge baseline.
                var iconX = 7F;
                var iconY = (Height - 9F) / 2F;
                e.Graphics.DrawRectangle(iconPen, iconX, iconY, 11F, 8F);
                e.Graphics.DrawLine(iconPen, iconX + 11F, iconY + 2F, iconX + 15F, iconY);
                e.Graphics.DrawLine(iconPen, iconX + 15F, iconY, iconX + 15F, iconY + 8F);
                e.Graphics.DrawLine(iconPen, iconX + 15F, iconY + 8F, iconX + 11F, iconY + 6F);

                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, _cameraId, Font,
                    new System.Drawing.Rectangle(28, 0,
                        Math.Max(1, Width - 28 - (_audioEnabled ? 12 : 4)), Height),
                    System.Drawing.Color.White,
                    System.Windows.Forms.TextFormatFlags.Left |
                    System.Windows.Forms.TextFormatFlags.VerticalCenter |
                    System.Windows.Forms.TextFormatFlags.NoPadding |
                    System.Windows.Forms.TextFormatFlags.SingleLine);

                // Match the WPF badge: show the green volume icon only when
                // this player is currently unmuted.
                if (_audioEnabled)
                {
                    // Use the Windows Segoe MDL2 volume glyph instead of
                    // hand-built arcs, which can rasterize as a clipped blob
                    // at the small native badge size.
                    using (var audioFont = new System.Drawing.Font("Segoe MDL2 Assets", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel))
                    using (var audioBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(34, 197, 94)))
                    {
                        const string volumeGlyph = "\uE995"; // Volume3
                        var textSize = System.Windows.Forms.TextRenderer.MeasureText(_cameraId, Font,
                            new System.Drawing.Size(int.MaxValue, Height),
                            System.Windows.Forms.TextFormatFlags.NoPadding |
                            System.Windows.Forms.TextFormatFlags.SingleLine);
                        var audioX = 28 + textSize.Width + 2;
                        System.Windows.Forms.TextRenderer.DrawText(e.Graphics, volumeGlyph, audioFont,
                            new System.Drawing.Rectangle(audioX, 0, Width - audioX - 3, Height),
                            audioBrush.Color,
                            System.Windows.Forms.TextFormatFlags.Left |
                            System.Windows.Forms.TextFormatFlags.VerticalCenter |
                            System.Windows.Forms.TextFormatFlags.NoPadding |
                            System.Windows.Forms.TextFormatFlags.SingleLine);
                    }
                }
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(System.Drawing.Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

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

        private sealed class AiDrawable
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public SolidColorBrush Brush { get; set; }
            public string Label { get; set; }
        }

        private struct AiLabelMetrics
        {
            public float Width;
            public float Height;
        }

        private readonly System.Windows.Forms.Panel _videoPanel = new System.Windows.Forms.Panel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.Black
        };
        // Keep the D3D sink in its own child HWND.  Native controls added to
        // _videoPanel can then sit above that child and are still clipped by
        // the WindowsFormsHost bounds.  This avoids using a top-level WPF
        // Popup for the camera badge (which could leak over another app).
        private readonly System.Windows.Forms.Panel _videoSurface = new System.Windows.Forms.Panel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.Black
        };
        private readonly CameraIdBadgeControl_v3 _cameraBadge = new CameraIdBadgeControl_v3();
        private bool _nativeBoundsSyncQueued;
        // Gst.Parse.Launch creates D3D11 decoder/sink resources in native
        // plugins. The per-player gate protects one tile, but does not make
        // concurrent creation across a camera wall safe. Serialize only this
        // short critical phase; the RTSP sessions then decode concurrently.
        private static readonly SemaphoreSlim PipelineConstructionGate = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<WhepPlayer_v3, byte> ActivePlayers =
            new ConcurrentDictionary<WhepPlayer_v3, byte>();
        private static int _preserveCameraAspectRatio;
        private int _disposed;
        private int _aiSeiReceivedLogged;

        /// <summary>
        /// Shared display preference for every active/new live camera player.
        /// False preserves the established wall behaviour: fill each grid cell.
        /// </summary>
        public static bool PreserveCameraAspectRatio
        {
            get { return Volatile.Read(ref _preserveCameraAspectRatio) == 1; }
            set
            {
                var requested = value ? 1 : 0;
                if (Interlocked.Exchange(ref _preserveCameraAspectRatio, requested) == requested)
                    return;

                foreach (var player in ActivePlayers.Keys)
                    player.ApplyAspectRatioPreference(value);
            }
        }
        private CancellationTokenSource _cancellation;
        private Pipeline _pipeline;
        private bool _isMuted;
        private readonly ConcurrentDictionary<Pipeline, GstBusMessagePump> _busMessagePumps =
            new ConcurrentDictionary<Pipeline, GstBusMessagePump>();
        private Element _aiOverlayElement;
        private Pipeline _aiOverlayPipeline;
        private Camera _camera;
        private CameraStreamInfo _selectedStream;
        private readonly SemaphoreSlim _connectionGate = new SemaphoreSlim(1, 1);
        private IntPtr _videoWindowHandle;
        private bool _useAlternateCodec;
        private bool _alternateCodecAttempted;
        private string _lastPipelineBuildError;
        // Fullscreen must switch only after the newly-created main pipeline
        // has decoded several real frames.  A fixed timer is inaccurate: it
        // delays healthy cameras and can still reveal an incomplete first
        // predictive frame from a slow camera.
        private const int StableVideoFrameCount = 6;
        private const int StableVideoFrameWaitMilliseconds = 1500;
        private int _decodedVideoFrameCount;
        // A prepared native window is not proof that the decoder has output a
        // valid image.  Keep the WindowsFormsHost hidden until both signals
        // have happened; otherwise its black/corrupt initial frame wins the
        // WPF airspace battle over the loading layer.
        private int _nativeVideoWindowPrepared;
        private int _playingStateRaised;
        private IntPtr _decodedFramePadHandle;
        private int _consecutiveCorruptedDecodedFrames;
        private int _decoderCorruptionPublished;
        private TaskCompletionSource<bool> _stableVideoFrames =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _aiOverlayEnabled;
        private readonly BlockingCollection<AiMetadataFrame_v3> _aiResult =
            new BlockingCollection<AiMetadataFrame_v3>(new ConcurrentQueue<AiMetadataFrame_v3>(), 1);
        // WebSocket metadata can arrive much less often than video frames.
        // Retain the latest frame briefly so the overlay stays visible between
        // messages, while the bounded queue still provides the base player's
        // non-blocking producer/consumer hand-off.
        private AiMetadataFrame_v3 _lastAiFrame;
        private readonly object _roiColorSync = new object();
        private readonly Dictionary<string, int> _roiColorIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object _aiRendererSync = new object();
        private SharpDX.Direct2D1.Factory _aiDrawFactory;
        private SharpDX.DirectWrite.Factory _aiTextFactory;
        private TextFormat _aiTextFormat;
        private RenderTargetProperties _aiRenderTargetProperties;
        // Drawing occurs on GStreamer's streaming thread.  Creating a
        // DirectWrite TextLayout for every bbox on every decoded frame was
        // the hottest managed allocation when several AI cameras were open.
        // Labels repeat across frames, so retain only their measured size.
        private readonly Dictionary<string, AiLabelMetrics> _aiLabelMetrics =
            new Dictionary<string, AiLabelMetrics>(StringComparer.Ordinal);
        private readonly Queue<string> _aiLabelMetricsOrder = new Queue<string>();
        private const int MaxAiLabelMetrics = 256;
        // AiBboxOverlay.tsx uses this same colour for every bbox, whether
        // the object is inside an ROI or not.
        private static readonly RawColor4 AiSuccessColor = new RawColor4(34f / 255f, 197f / 255f, 94f / 255f, 1f);
        // Exact palette and index order used by the deployed Web live/playback
        // renderer: yellow, orange, blue, pink; then repeats.
        private static readonly RawColor4[] AiRoiColors =
        {
            new RawColor4(1f, 1f, 0f, 1f),
            new RawColor4(1f, 180f / 255f, 0f, 1f),
            new RawColor4(0f, 120f / 255f, 1f, 1f),
            new RawColor4(1f, 120f / 255f, 220f / 255f, 1f)
        };
        private static readonly RawColor4 AiErrorColor = new RawColor4(239f / 255f, 68f / 255f, 68f / 255f, 1f);
        private static readonly RawColor4 AiLabelTextColor = new RawColor4(3f / 255f, 19f / 255f, 10f / 255f, 1f);

        public event EventHandler<WhepPlaybackStateChangedEventArgs_v3> PlaybackStateChanged;
        public event EventHandler VideoMouseEnter;
        public event EventHandler VideoMouseMove;
        public event EventHandler VideoMouseLeave;

        public WhepPlayer_v3()
        {
            InitializeComponent();
            ActivePlayers.TryAdd(this, 0);
            VideoHost.Child = _videoPanel;
            _videoPanel.Controls.Add(_videoSurface);
            _cameraBadge.BackColor = System.Drawing.Color.FromArgb(21, 47, 72);
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
            _videoSurface.MouseEnter += (s, e) => VideoMouseEnter?.Invoke(this, EventArgs.Empty);
            _videoSurface.MouseMove += (s, e) => VideoMouseMove?.Invoke(this, EventArgs.Empty);
            _videoSurface.MouseLeave += (s, e) => VideoMouseLeave?.Invoke(this, EventArgs.Empty);
            // WindowsFormsHost may receive its final arrange after the parent grid
            // has already changed rows/columns. Queue the native child resize at
            // Render priority so d3d11videosink always renders inside this tile.
            VideoHost.SizeChanged += (s, e) => QueueNativeVideoHostSynchronization();
            SizeChanged += (s, e) => QueueNativeVideoHostSynchronization();
            Unloaded += (s, e) => Dispose();
        }

        private void QueueNativeVideoHostSynchronization()
        {
            if (_nativeBoundsSyncQueued || _videoPanel.IsDisposed) return;
            _nativeBoundsSyncQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _nativeBoundsSyncQueued = false;
                SynchronizeNativeVideoHost();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        public bool AiOverlayEnabled
        {
            get { return Interlocked.CompareExchange(ref _aiOverlayEnabled, 0, 0) != 0; }
            set { Interlocked.Exchange(ref _aiOverlayEnabled, value ? 1 : 0); }
        }

        // Same non-blocking hand-off as RtspPlayer.Send2Draw: the streaming
        // thread only renders the newest metadata frame and is never delayed
        // by a network/UI producer.
        public void Send2Draw(AiMetadataFrame_v3 frame)
        {
            if (frame == null || !AiOverlayEnabled) return;
            // Snapshot export must not depend on the native draw callback.
            // That callback is paused when a tile is hidden or being promoted
            // to the main stream, while metadata continues to arrive.
            Interlocked.Exchange(ref _lastAiFrame, frame);
            AiMetadataFrame_v3 ignored;
            while (_aiResult.TryTake(out ignored)) { }
            _aiResult.TryAdd(frame);
        }

        public void ClearAiMetadata()
        {
            AiMetadataFrame_v3 ignored;
            while (_aiResult.TryTake(out ignored)) { }
            Interlocked.Exchange(ref _lastAiFrame, null);
        }

        private void InitializeAiOverlayRenderer()
        {
            if (_aiDrawFactory != null) return;
            lock (_aiRendererSync)
            {
                if (_aiDrawFactory != null) return;
                _aiDrawFactory = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);
                _aiTextFactory = new SharpDX.DirectWrite.Factory();
                _aiTextFormat = new TextFormat(_aiTextFactory, "Segoe UI", 19f) { WordWrapping = WordWrapping.NoWrap };
                _aiRenderTargetProperties = new RenderTargetProperties(
                    new PixelFormat(Format.R8G8B8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));
            }
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
        /// Forces WindowsFormsHost and its child HWND to consume the current
        /// WPF arrange bounds. This is required after a borderless window is
        /// moved between monitors or leaves fullscreen: the D3D sink can keep
        /// painting the previous native rectangle even though WPF has already
        /// arranged the tile in its new cell.
        /// </summary>
        public void SynchronizeNativeVideoHost()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(SynchronizeNativeVideoHost),
                    System.Windows.Threading.DispatcherPriority.Render);
                return;
            }
            if (_videoPanel.IsDisposed) return;

            try
            {
                VideoHost.InvalidateMeasure();
                VideoHost.InvalidateArrange();
                VideoHost.UpdateLayout();
                _videoPanel.SuspendLayout();
                _videoSurface.SuspendLayout();
                // Do not rely solely on Dock layout here. During a fullscreen,
                // monitor, or grid transition it can be one render pass behind
                // WPF. Assigning the client rectangle explicitly prevents the
                // native GStreamer child from retaining the previous tile size.
                _videoSurface.Dock = System.Windows.Forms.DockStyle.None;
                _videoSurface.Bounds = _videoPanel.ClientRectangle;
                _videoSurface.Dock = System.Windows.Forms.DockStyle.Fill;
                _videoPanel.PerformLayout();
                _videoSurface.PerformLayout();
                _cameraBadge.BringToFront();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            finally
            {
                if (!_videoPanel.IsDisposed)
                    _videoPanel.ResumeLayout(true);
                if (!_videoSurface.IsDisposed)
                    _videoSurface.ResumeLayout(true);
            }
        }

        /// <summary>
        /// Keeps the native pipeline alive without allowing either its video
        /// surface or its WPF status layer to appear.  This is used while a
        /// fullscreen main stream warms up behind the already-playing sub
        /// stream.
        /// </summary>
        public void SetPresentationVisible(bool visible)
        {
            // MainPlayer remains connected as a warm-up pipeline, but must
            // not participate in WPF measure/arrange when it is not the
            // visible presentation. This removes a second native HWND layout
            // participant for every Live camera tile during resize.
            if (visible)
            {
                VideoHost.Visibility = Visibility.Visible;
                _videoPanel.Visible = true;
            }
            else
            {
                VideoHost.Visibility = Visibility.Collapsed;
                _videoPanel.Visible = false;
                _cameraBadge.Visible = false;
                StatusPanel.Visibility = Visibility.Collapsed;
            }
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
            if (_videoPanel.IsDisposed || _cameraBadge.IsDisposed || !_videoPanel.IsHandleCreated)
                return;
            if (_videoPanel.InvokeRequired)
            {
                try { _videoPanel.BeginInvoke(new Action(() => SetCameraBadge(cameraId, visible, connected, error))); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            try
            {
            // This label is a sibling of the D3D sink HWND, not content drawn
            // into GStreamer.  It stays inside the current tile and cannot
            // be placed above another window or a neighbouring tile.
            var statusColor = error
                ? System.Drawing.Color.FromArgb(239, 68, 68)   // VmsError
                : connected
                    ? System.Drawing.Color.FromArgb(34, 197, 94) // VmsSuccess
                    : System.Drawing.Color.FromArgb(245, 158, 11); // VmsWarning
            // Only show the volume glyph when the current pipeline actually
            // exposes an audio stream and that stream is not muted.
            _cameraBadge.SetBadge(cameraId, statusColor, !_isMuted && HasAudio);
            _cameraBadge.Visible = visible && !string.IsNullOrWhiteSpace(cameraId) && _videoPanel.Visible;
            if (_cameraBadge.Visible)
                _cameraBadge.BringToFront();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public System.Threading.Tasks.Task ReconnectAsync() { return ConnectAsync(); }

        /// <summary>
        /// Waits for a small run of decoded frames from the current pipeline.
        /// This is used only for the invisible fullscreen-main warm-up; normal
        /// playback remains non-blocking.
        /// </summary>
        public async System.Threading.Tasks.Task WaitForStableVideoFramesAsync()
        {
            var completion = Volatile.Read(ref _stableVideoFrames);
            if (Volatile.Read(ref _decodedVideoFrameCount) >= StableVideoFrameCount)
                return;

            await System.Threading.Tasks.Task.WhenAny(
                completion.Task,
                System.Threading.Tasks.Task.Delay(StableVideoFrameWaitMilliseconds)).ConfigureAwait(true);
        }

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
        /// Cancels a pending connect and waits until it has released the
        /// connection gate before disposing the native pipeline. A plain
        /// cancellation is insufficient: Parse.Launch cannot be interrupted,
        /// so an old pipeline could otherwise finish building after Clear All
        /// and overlap the next camera-wall cycle.
        /// </summary>
        public async System.Threading.Tasks.Task DisconnectPipelineAsync()
        {
            var cancellation = _cancellation;
            if (cancellation != null)
                cancellation.Cancel();

            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    if (cancellation != null)
                        cancellation.Dispose();
                }

                await System.Threading.Tasks.Task.Run(() =>
                {
                    DisposePipeline();
                    ReleaseInactiveAiResources();
                }).ConfigureAwait(false);
            }
            finally
            {
                _connectionGate.Release();
            }
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
            ReleaseInactiveAiResources();
        }

        /// <summary>
        /// The tile can remain in an empty grid slot after its camera is removed.
        /// Do not retain the latest AI frame or Direct2D/DirectWrite allocations
        /// in that case; they are recreated lazily on the next AI frame.
        /// </summary>
        private void ReleaseInactiveAiResources()
        {
            if (System.Threading.Volatile.Read(ref _pipeline) != null) return;

            ClearAiMetadata();
            lock (_roiColorSync)
                _roiColorIndices.Clear();

            lock (_aiRendererSync)
            {
                // A new connection may have completed while this cleanup was
                // waiting for the renderer lock. Its resources belong to the
                // new pipeline and must remain intact.
                if (System.Threading.Volatile.Read(ref _pipeline) != null) return;

                _aiLabelMetrics.Clear();
                _aiLabelMetricsOrder.Clear();
                _aiTextFormat?.Dispose();
                _aiTextFormat = null;
                _aiTextFactory?.Dispose();
                _aiTextFactory = null;
                _aiDrawFactory?.Dispose();
                _aiDrawFactory = null;
            }
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (Volatile.Read(ref _disposed) != 0 || _camera == null) return;

            _cancellation?.Cancel();
            _cancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var selectedCamera = _camera;
            _ = RefreshRoiColorIndicesAsync(selectedCamera.camID, cancellation.Token);

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

                // Capture a valid, laid-out HWND while on the dispatcher.
                // Creating d3d11videosink while WindowsFormsHost is still at
                // 0x0 during a multi-monitor fullscreen transition can make
                // the native sink fail to open its window (and occasionally
                // terminate the process in the driver/plugin path).
                _videoWindowHandle = VideoHost.Visibility == Visibility.Visible
                    ? await WaitForVisibleVideoHostAsync(cancellation.Token).ConfigureAwait(true)
                    : GetVideoWindowHandle();
                await _connectionGate.WaitAsync(cancellation.Token).ConfigureAwait(true);
                bool pipelineCreated;
                try
                {
                    // Creating all native D3D11 sinks at once can terminate
                    // the process in the video driver/plugin path. Keep only
                    // construction serialized; each completed pipeline starts
                    // immediately and is never held back by slow cameras.
                    await PipelineConstructionGate.WaitAsync(cancellation.Token).ConfigureAwait(true);
                    try
                    {
                        if (cancellation.IsCancellationRequested ||
                            !ReferenceEquals(_cancellation, cancellation) ||
                            !ReferenceEquals(_camera, selectedCamera))
                            return;

                        pipelineCreated = await System.Threading.Tasks.Task.Run(() => CreatePipeline(
                            source.Url,
                            source.IsH264,
                            _videoWindowHandle), cancellation.Token).ConfigureAwait(true);
                    }
                    finally
                    {
                        PipelineConstructionGate.Release();
                    }
                    if (pipelineCreated && (Volatile.Read(ref _disposed) != 0 ||
                        cancellation.IsCancellationRequested ||
                        !ReferenceEquals(_cancellation, cancellation) ||
                        !ReferenceEquals(_camera, selectedCamera)))
                    {
                        // Native construction cannot be cancelled once GStreamer
                        // enters Parse.Launch. If the tile was closed meanwhile,
                        // tear down the just-created pipeline before releasing the
                        // per-player gate so it cannot survive a disposed view.
                        await System.Threading.Tasks.Task.Run(() => DisposePipeline()).ConfigureAwait(true);
                        return;
                    }
                }
                finally
                {
                    _connectionGate.Release();
                }
                if (!pipelineCreated)
                {
                    PublishError(WhepPlaybackErrorKind_v3.Decoder,
                        string.IsNullOrWhiteSpace(_lastPipelineBuildError)
                            ? "GStreamer could not create the direct playback pipeline."
                            : _lastPipelineBuildError,
                        false);
                    return;
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

        private bool CreatePipeline(string rtspUrl, bool isH264, IntPtr videoWindowHandle)
        {
            Pipeline pipeline = null;
            _lastPipelineBuildError = null;
            try
            {
                _videoWindowHandle = videoWindowHandle;
                ResetStableVideoFrameWait();
            // Keep the same Direct3D11 decode and render path as the original
            // V3 client.  This avoids software decode and the CPU-side
            // videoconvert copy once several cameras are open.  The URL and
            // codec are resolved together above so that the parser and decoder
            // always match the actual selected RTSP stream.
                // Keep the encoded access-unit stream in Annex-B/NAL form
                // until after identity.  AI cameras carry their metadata in
                // Retain the production-tested RTSP pipeline from the legacy
                // iVista client. Its element order and timings are intentional
                // and have been exercised against the deployed camera relays.
                var pipelineText = isH264
                    ? "rtspsrc protocols=tcp name=videoSource latency=2000 timeout=300000 do-retransmission=false videoSource. ! " +
                      "queue leaky=1 name=video-queue ! watchdog timeout=300000 ! rtph264depay ! video/x-h264, stream-format=byte-stream, alignment=nal " +
                      "! identity name=identity ! h264parse ! video/x-h264, stream-format=(string)avc, alignment=(string)au ! d3d11h264dec qos=false ! d3d11convert ! queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink name=videoSink force-aspect-ratio=" +
                      (PreserveCameraAspectRatio ? "true" : "false") +
                      " async=false sync=false qos=false videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample ! volume name=audioVolume mute=" + (_isMuted ? "true" : "false") + " ! wasapisink async=false sync=false"
                    : "rtspsrc name=videoSource latency=2000 timeout=5000 videoSource. ! " +
                      "queue leaky=1 name=video-queue ! watchdog timeout=15000 ! rtph265depay ! video/x-h265, stream-format=byte-stream, alignment=nal " +
                      "! identity name=identity ! h265parse ! video/x-h265, stream-format=(string)hvc1, alignment=(string)au ! d3d11h265dec ! d3d11convert ! queue leaky=1 ! d3d11overlay name=videoOverlay ! d3d11videosink name=videoSink force-aspect-ratio=" +
                      (PreserveCameraAspectRatio ? "true" : "false") +
                      " async=false sync=false qos=false videoSource. ! queue leaky=1 name=audio-queue ! application/x-rtp,media=audio ! decodebin ! audioconvert ! audioresample ! volume name=audioVolume mute=" + (_isMuted ? "true" : "false") + " ! wasapisink async=false sync=false";
                pipeline = (Pipeline)Parse.Launch(pipelineText);
                if (pipeline == null)
                    throw new InvalidOperationException("GStreamer returned an empty playback pipeline.");

                // The overlay paints the most recent SEI metadata on each
                // decoded video frame.
                var aiOverlay = pipeline.GetByName("videoOverlay");
                if (aiOverlay == null)
                    throw new InvalidOperationException("GStreamer did not create the AI video overlay.");
                aiOverlay.Connect("draw", Draw);
                lock (_aiRendererSync)
                {
                    _aiOverlayElement = aiOverlay;
                    _aiOverlayPipeline = pipeline;
                }

                using (var metadataIdentity = pipeline.GetByName("identity"))
                {
                    if (metadataIdentity == null)
                        throw new InvalidOperationException("GStreamer did not create the AI metadata identity element.");
                    using (var metadataPad = metadataIdentity.GetStaticPad("src"))
                    {
                        if (metadataPad == null)
                            throw new InvalidOperationException("GStreamer did not create the AI metadata source pad.");
                        metadataPad.AddProbe(PadProbeType.Buffer, OnAiMetadataBuffer);
                        LoggerManager.LogInfo("Live View _v3 AI SEI reader attached to RTSP pipeline.");
                    }
                }

                using (var source = pipeline.GetByName("videoSource"))
                {
                    if (source == null)
                        throw new InvalidOperationException("GStreamer did not create the RTSP source element.");
                    source["location"] = rtspUrl;
                }
                AttachBusMessagePump(pipeline);
                // Make the active pipeline visible before entering Playing.
                // A fast RTSP source can decode and invoke the frame probe
                // synchronously during SetState; publishing it afterwards
                // loses the only Playing notification and the tile times out.
                _pipeline = pipeline;
                if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                    throw new InvalidOperationException("GStreamer could not start the direct RTSP playback pipeline.");

                return true;
            }
            catch (Exception ex)
            {
                _lastPipelineBuildError = ex.Message;
                LoggerManager.LogException(ex, "Live View _v3 GStreamer pipeline creation failed");
                if (ReferenceEquals(_pipeline, pipeline))
                    _pipeline = null;
                if (pipeline != null) DisposePipelineInstance(pipeline);
                return false;
            }
        }

        private PadProbeReturn OnAiMetadataBuffer(Pad pad, PadProbeInfo info)
        {
            var buffer = info.Buffer;
            if (buffer == null || !AiOverlayEnabled)
                return PadProbeReturn.Ok;

            MapInfo mapInfo;
            if (!buffer.Map(out mapInfo, Gst.MapFlags.Read))
                return PadProbeReturn.Ok;
            try
            {
                ParseAiSeiNal(mapInfo.Data);
            }
            catch (Exception ex)
            {
                // Metadata can be malformed on an individual frame.  Keep
                // video playback alive and log the exact pipeline issue.
                LoggerManager.LogException(ex, "Live View _v3 AI SEI parse failed");
            }
            finally
            {
                buffer.Unmap(mapInfo);
            }
            return PadProbeReturn.Ok;
        }

        private PadProbeReturn OnDecodedVideoBuffer(Pad pad, PadProbeInfo info)
        {
            var buffer = info.Buffer;
            if (buffer == null || pad == null || pad.Handle != _decodedFramePadHandle)
                return PadProbeReturn.Ok;

            if ((buffer.Flags & BufferFlags.Corrupted) == 0)
            {
                Interlocked.Exchange(ref _consecutiveCorruptedDecodedFrames, 0);
                // Count readiness at the decoded-buffer boundary rather than
                // from the overlay draw callback. This is available for every
                // camera, including streams without AI metadata.
                ObserveDecodedVideoFrame();
                return PadProbeReturn.Ok;
            }

            // A relay can begin in the middle of a GOP. The first H.264/H.265
            // frames are then legitimately undecodable until the next IDR.
            // Suppress damaged output but keep the source alive for keyframe
            // recovery instead of disconnecting after three startup frames.
            Interlocked.Increment(ref _consecutiveCorruptedDecodedFrames);
            return PadProbeReturn.Drop;
        }

        private void ParseAiSeiNal(byte[] data)
        {
            if (data == null || data.Length < 5) return;
            var offset = data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1 ? 4 :
                         data[0] == 0 && data[1] == 0 && data[2] == 1 ? 3 : -1;
            if (offset < 0 || offset >= data.Length) return;

            var h264Sei = (data[offset] & 0x1f) == 6;
            var h265Type = (data[offset] >> 1) & 0x3f;
            var h265Sei = h265Type == 39 || h265Type == 40;
            if (!h264Sei && !h265Sei) return;

            var ptr = offset + (h264Sei ? 1 : 2);
            var payloadType = ReadSeiValue(data, ref ptr);
            var payloadSize = ReadSeiValue(data, ref ptr);
            // The original RTSP player accepts the encoder's UUID + JSON
            // payload regardless of the SEI type field, so retain that
            // compatibility with existing camera firmware.
            if (payloadType < 0 || payloadSize < 20 || ptr + payloadSize > data.Length) return;

            var textLength = BitConverter.ToUInt32(data, ptr + 16);
            if (textLength > payloadSize - 20) return;
            PublishAiMetadata(Encoding.UTF8.GetString(data, ptr + 20, (int)textLength));
        }

        private static int ReadSeiValue(byte[] data, ref int offset)
        {
            var value = 0;
            while (offset < data.Length && data[offset] == 0xff)
            {
                value += 255;
                offset++;
            }
            if (offset >= data.Length) return -1;
            return value + data[offset++];
        }

        private void PublishAiMetadata(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            var frame = JsonConvert.DeserializeObject<MetaFrame>(json);
            if (frame == null || frame.AiResults == null || frame.ImageInfo == null ||
                frame.ImageInfo.ImageWidth <= 0 || frame.ImageInfo.ImageHeight <= 0) return;

            var minConfidence = GlobalSystem.Instance.MinConfidence;
            var objects = frame.AiResults
                .Where(x => x != null && x.Bbox != null && x.Confidence >= minConfidence)
                .Select(ToOverlayObject)
                .ToList();
            if (objects.Count == 0) return;

            if (Interlocked.Exchange(ref _aiSeiReceivedLogged, 1) == 0)
                LoggerManager.LogInfo("Live View _v3 AI SEI metadata received for camera " +
                                      (_camera == null ? "(unknown)" : _camera.camID) + ".");

            Send2Draw(new AiMetadataFrame_v3
            {
                CameraId = _camera == null ? null : _camera.camID,
                SourceWidth = frame.ImageInfo.ImageWidth,
                SourceHeight = frame.ImageInfo.ImageHeight,
                Objects = objects,
                ReceivedAtUtc = System.DateTime.UtcNow
            });
        }

        private static AiMetadataBox_v3 ToOverlayObject(AIResult result)
        {
            var roi = result.ObjectAnalysisList == null ? null : result.ObjectAnalysisList.FirstOrDefault();
            string roiId = null;
            string dwell = null;
            if (roi != null && roi.RoiInside != null)
            {
                var inside = roi.RoiInside.FirstOrDefault(pair => pair.Value);
                if (!string.IsNullOrWhiteSpace(inside.Key))
                {
                    roiId = inside.Key;
                    float seconds;
                    if (roi.RoiDwellSeconds != null && roi.RoiDwellSeconds.TryGetValue(roiId, out seconds))
                        dwell = string.Format("{0:F1}s", seconds);
                }
            }
            return new AiMetadataBox_v3
            {
                Label = string.IsNullOrWhiteSpace(result.Caption) ? result.MetaType : result.Caption,
                Confidence = result.Confidence,
                IsBlacklist = result.IsBlacklist,
                HasPixelBounds = true,
                PixelLeft = result.Bbox.Left,
                PixelTop = result.Bbox.Top,
                PixelWidth = result.Bbox.Width,
                PixelHeight = result.Bbox.Height,
                IsInsideRoi = !string.IsNullOrWhiteSpace(roiId),
                RoiId = roiId,
                RoiDwellSecondsInfo = dwell
            };
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

        // Kept intentionally in the same form as RtspPlayer.Draw. In
        // particular, args.Args[0] belongs to GStreamer and must not be
        // disposed from this callback.
        protected void Draw(object o, SignalArgs args)
        {
            AiMetadataFrame_v3 incomingFrame;
            if (_aiResult.TryTake(out incomingFrame, 0))
                Interlocked.Exchange(ref _lastAiFrame, incomingFrame);

            var frame = _lastAiFrame;
            if (frame == null || frame.Objects == null || frame.Objects.Count == 0 ||
                frame.ReceivedAtUtc == default(System.DateTime) ||
                System.DateTime.UtcNow - frame.ReceivedAtUtc > TimeSpan.FromSeconds(5) ||
                args == null || args.Args == null || args.Args.Length < 2)
                return;

            var texturePointer = args.Args[1] is IntPtr ? (IntPtr)args.Args[1] : IntPtr.Zero;
            if (texturePointer == IntPtr.Zero) return;

            RenderTargetView renderTargetView = null;
            // The pipeline teardown path can run while GStreamer is still
            // completing this draw signal. Keep the Direct2D factories alive
            // for the complete native draw transaction.
            lock (_aiRendererSync)
            {
            try
            {
                InitializeAiOverlayRenderer();
                renderTargetView = new RenderTargetView(texturePointer);
                using (var resource = renderTargetView.Resource)
                using (var surface = resource.QueryInterface<Surface>())
                using (var target = new RenderTarget(_aiDrawFactory, surface, _aiRenderTargetProperties))
                using (var successBrush = new SolidColorBrush(target, AiSuccessColor))
                using (var roiBrush0 = new SolidColorBrush(target, AiRoiColors[0]))
                using (var roiBrush1 = new SolidColorBrush(target, AiRoiColors[1]))
                using (var roiBrush2 = new SolidColorBrush(target, AiRoiColors[2]))
                using (var roiBrush3 = new SolidColorBrush(target, AiRoiColors[3]))
                using (var errorBrush = new SolidColorBrush(target, AiErrorColor))
                using (var labelBrush = new SolidColorBrush(target, AiLabelTextColor))
                {
                    var description = surface.Description;
                    var sourceWidth = frame.SourceWidth > 0 ? frame.SourceWidth : description.Width;
                    var sourceHeight = frame.SourceHeight > 0 ? frame.SourceHeight : description.Height;
                    if (sourceWidth <= 0 || sourceHeight <= 0) return;

                    var ratioX = description.Width / (float)sourceWidth;
                    var ratioY = description.Height / (float)sourceHeight;
                    target.BeginDraw();
                    try
                    {
                        var drawables = new List<AiDrawable>(frame.Objects.Count);
                        foreach (var item in frame.Objects)
                        {
                            float left, top, width, height;
                            if (!TryMapAiBounds(item, sourceWidth, sourceHeight, ratioX, ratioY,
                                out left, out top, out width, out height)) continue;

                            var boxBrush = item.IsBlacklist ? errorBrush :
                                (item.IsInsideRoi ? GetRoiBrush(GetRoiColorIndex(item.RoiId), roiBrush0,
                                    roiBrush1, roiBrush2, roiBrush3) : successBrush);
                            // Match WebApp: show the detection/plate name, not
                            // the confidence percentage.
                            var label = (item.Label ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                            label = string.Join(" ", label.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, "object", StringComparison.OrdinalIgnoreCase))
                                label = "car";
                            if (item.IsInsideRoi)
                                label += string.IsNullOrWhiteSpace(item.RoiDwellSecondsInfo)
                                    ? " in ROI" : " - " + item.RoiDwellSecondsInfo;

                            drawables.Add(new AiDrawable
                            {
                                Left = left, Top = top, Width = width, Height = height,
                                Brush = boxBrush, Label = label
                            });
                        }

                        // Keep labels above every box: a later box must never
                        // cover a plate label belonging to an earlier object.
                        foreach (var drawable in drawables)
                            target.DrawRectangle(new RectangleF(drawable.Left, drawable.Top,
                                drawable.Width, drawable.Height), drawable.Brush, 2.5f);

                        foreach (var drawable in drawables)
                        {
                            var metrics = GetAiLabelMetrics(drawable.Label, description.Width);
                            var labelWidth = Math.Min(description.Width - drawable.Left, metrics.Width + 10);
                            var labelHeight = metrics.Height + 6;
                            var labelTop = Math.Max(0, drawable.Top - labelHeight);
                            target.FillRectangle(new RectangleF(drawable.Left, labelTop, labelWidth, labelHeight), drawable.Brush);
                            target.DrawText(drawable.Label, _aiTextFormat,
                                new RectangleF(drawable.Left + 5, labelTop + 2, Math.Max(1, labelWidth - 5), labelHeight), labelBrush);
                        }
                    }
                    finally { target.EndDraw(); }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("Live View _v3 AI overlay draw skipped: " + ex.Message);
            }
            finally
            {
                if (renderTargetView != null)
                {
                    // Texture lifetime belongs to GStreamer; mirror the base
                    // safeguard so SharpDX does not release its native handle.
                    renderTargetView.NativePointer = IntPtr.Zero;
                    renderTargetView.Dispose();
                }
            }
            }
        }

        private void ApplyAspectRatioPreference(bool preserveAspectRatio)
        {
            var pipeline = _pipeline;
            if (pipeline == null) return;

            Element sink = null;
            try
            {
                sink = pipeline.GetByName("videoSink");
                if (sink != null)
                    sink["force-aspect-ratio"] = preserveAspectRatio;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể cập nhật tỉ lệ hiển thị camera");
            }
            finally
            {
                sink?.Dispose();
            }
        }

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
            if (_cameraBadge != null && !_cameraBadge.IsDisposed)
                _cameraBadge.Invalidate();
            var pipeline = _pipeline;
            if (pipeline == null) return;
            var volume = pipeline.GetByName("audioVolume");
            if (volume == null) return;
            try { volume["mute"] = muted; }
            finally { volume.Dispose(); }
        }

        public bool HasAudio
        {
            get
            {
                var pipeline = _pipeline;
                if (pipeline == null) return false;
                Element volume = null;
                try { volume = pipeline.GetByName("audioVolume"); return volume != null; }
                catch { return false; }
                finally { volume?.Dispose(); }
            }
        }

        public bool SetVolume(double volumeLevel)
        {
            var pipeline = _pipeline;
            if (pipeline == null) return false;
            Element volume = null;
            try
            {
                volume = pipeline.GetByName("audioVolume");
                if (volume == null) return false;
                volume["volume"] = Math.Max(0d, Math.Min(1d, volumeLevel));
                volume["mute"] = volumeLevel <= 0.001d;
                return true;
            }
            catch { return false; }
            finally { volume?.Dispose(); }
        }

        public bool TrySaveSnapshot(out string savedPath)
        {
            savedPath = null;
            if (_videoPanel.IsDisposed || _videoPanel.Width < 2 || _videoPanel.Height < 2) return false;
            try
            {
                var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                System.IO.Directory.CreateDirectory(downloads);
                var safeId = string.Concat((_camera?.camID ?? "camera").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                savedPath = System.IO.Path.Combine(downloads, safeId + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                using (var bitmap = new System.Drawing.Bitmap(_videoPanel.Width, _videoPanel.Height))
                {
                    var source = _videoPanel.PointToScreen(System.Drawing.Point.Empty);
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(source, System.Drawing.Point.Empty, bitmap.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                    bitmap.Save(savedPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể chụp ảnh Live View " + (_camera?.camID ?? string.Empty));
                savedPath = null;
                return false;
            }
        }

        public async System.Threading.Tasks.Task<string> TrySaveSourceSnapshotAsync()
        {
            var source = GetSnapshotRtspSource();
            if (source == null || string.IsNullOrWhiteSpace(source.Url)) return null;

            var ffmpeg = ResolveFfmpegExecutable();
            if (string.IsNullOrWhiteSpace(ffmpeg)) return null;

            var outputPath = CreateSnapshotOutputPath();
            if (string.IsNullOrWhiteSpace(outputPath)) return null;

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-hide_banner -loglevel error -y -rtsp_transport tcp -i " + QuoteProcessArgument(source.Url) +
                        " -map 0:v:0 -frames:v 1 -an -c:v png " + QuoteProcessArgument(outputPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null) return null;
                    var errorRead = process.StandardError.ReadToEndAsync();
                    var outputRead = process.StandardOutput.ReadToEndAsync();
                    var exited = await System.Threading.Tasks.Task.Run(() => process.WaitForExit(15000));
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }
                    await System.Threading.Tasks.Task.WhenAll(errorRead, outputRead);
                    if (process.ExitCode != 0 || !System.IO.File.Exists(outputPath) || new System.IO.FileInfo(outputPath).Length == 0)
                    {
                        try { if (System.IO.File.Exists(outputPath)) System.IO.File.Delete(outputPath); } catch { }
                        return null;
                    }
                }
                DrawAiOnSnapshotFile(outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể xuất frame gốc Live View " + (_camera?.camID ?? string.Empty));
                return null;
            }
        }

        private RtspSource_v3 GetSnapshotRtspSource()
        {
            var camera = _camera;
            if (camera == null) return null;

            // The grid may deliberately display the lightweight raw substream.
            // For an AI camera, its paired AI RTSP source is the authoritative
            // image for export and retains any server-composited detections.
            var hasAi = camera.HasAIStream ||
                string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) ||
                (camera.Streams != null && camera.Streams.Any(stream => stream != null && stream.IsAiMode == true));
            if (hasAi)
            {
                var isMain = _selectedStream != null && string.Equals(_selectedStream.StreamType,
                    "main", StringComparison.OrdinalIgnoreCase);
                var aiUrl = isMain ? camera.RtspUrlMainAI : camera.RtspUrlAI;
                if (!string.IsNullOrWhiteSpace(aiUrl) &&
                    System.Uri.IsWellFormedUriString(aiUrl, System.UriKind.Absolute))
                {
                    return new RtspSource_v3
                    {
                        Url = aiUrl,
                        IsH264 = isMain ? camera.IsH264MainAI : camera.IsH264AI
                    };
                }
            }
            return GetRtspSource(camera, _selectedStream);
        }

        private string CreateSnapshotOutputPath()
        {
            var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            System.IO.Directory.CreateDirectory(downloads);
            var safeId = string.Concat((_camera?.camID ?? "camera").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return System.IO.Path.Combine(downloads, safeId + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        }

        private static string ResolveFfmpegExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            var candidates = new[] { configured, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"), @"C:\ffmpeg-8.1-essentials_build\bin\ffmpeg.exe" };
            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path));
        }

        private static string QuoteProcessArgument(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }

        private void DrawAiOnSnapshotFile(string outputPath)
        {
            var frame = System.Threading.Interlocked.CompareExchange(ref _lastAiFrame, null, null);
            if (frame == null || frame.Objects == null || frame.Objects.Count == 0)
                return;

            var temporaryPath = outputPath + ".ai.tmp";
            try
            {
                using (var bitmap = new System.Drawing.Bitmap(outputPath))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    // Some metadata publishers omit debug.source_width/source_height.
                    // The bbox payload is still normalized, so the native source
                    // PNG is the correct fallback coordinate space.
                    var sourceWidth = frame.SourceWidth > 0 ? frame.SourceWidth : bitmap.Width;
                    var sourceHeight = frame.SourceHeight > 0 ? frame.SourceHeight : bitmap.Height;
                    var scaleX = bitmap.Width / (float)sourceWidth;
                    var scaleY = bitmap.Height / (float)sourceHeight;
                    var lineWidth = Math.Max(2f, 2.5f * Math.Min(scaleX, scaleY));
                    using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, Math.Max(11f, 12f * Math.Min(scaleX, scaleY)), System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel))
                    using (var format = new System.Drawing.StringFormat(System.Drawing.StringFormatFlags.NoWrap | System.Drawing.StringFormatFlags.NoClip))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.Lime, lineWidth))
                    using (var background = new System.Drawing.SolidBrush(System.Drawing.Color.Lime))
                    using (var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(3, 19, 10)))
                    {
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        foreach (var item in frame.Objects)
                        {
                            if (item == null) continue;
                            var left = item.HasPixelBounds ? item.PixelLeft : item.Left * sourceWidth;
                            var top = item.HasPixelBounds ? item.PixelTop : item.Top * sourceHeight;
                            var width = item.HasPixelBounds ? item.PixelWidth : item.Width * sourceWidth;
                            var height = item.HasPixelBounds ? item.PixelHeight : item.Height * sourceHeight;
                            if (width <= 0 || height <= 0) continue;
                            var rect = new System.Drawing.RectangleF((float)(left * scaleX), (float)(top * scaleY), (float)(width * scaleX), (float)(height * scaleY));
                            graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                            var label = string.IsNullOrWhiteSpace(item.Label) ? "AI" : item.Label;
                            var size = graphics.MeasureString(label, font, int.MaxValue, format);
                            var labelY = Math.Max(0, rect.Y - size.Height - 4);
                            graphics.FillRectangle(background, rect.X, labelY, size.Width + 8, size.Height + 4);
                            graphics.DrawString(label, font, foreground, rect.X + 4, labelY + 2, format);
                        }
                    }
                    bitmap.Save(temporaryPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                System.IO.File.Copy(temporaryPath, outputPath, true);
            }
            catch (Exception ex) { LoggerManager.LogException(ex, "Không thể vẽ AI lên ảnh gốc Live View"); }
            finally { try { if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath); } catch { } }
        }

        private void ResetStableVideoFrameWait()
        {
            Interlocked.Exchange(ref _decodedVideoFrameCount, 0);
            Interlocked.Exchange(ref _nativeVideoWindowPrepared, 0);
            Interlocked.Exchange(ref _playingStateRaised, 0);
            Interlocked.Exchange(ref _decodedFramePadHandle, IntPtr.Zero);
            Interlocked.Exchange(ref _consecutiveCorruptedDecodedFrames, 0);
            Interlocked.Exchange(ref _decoderCorruptionPublished, 0);
            var replacement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = Interlocked.Exchange(ref _stableVideoFrames, replacement);
            previous.TrySetResult(true);
        }

        private void ObserveDecodedVideoFrame()
        {
            if (Interlocked.Increment(ref _decodedVideoFrameCount) != StableVideoFrameCount)
                return;
            Volatile.Read(ref _stableVideoFrames).TrySetResult(true);
            PublishPlayingWhenVideoIsStable();
        }

        private void PublishPlayingWhenVideoIsStable()
        {
            if (Volatile.Read(ref _nativeVideoWindowPrepared) == 0 ||
                Volatile.Read(ref _decodedVideoFrameCount) < StableVideoFrameCount ||
                Interlocked.CompareExchange(ref _playingStateRaised, 1, 0) != 0)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // A late callback from a just-disposed pipeline must not
                // reveal a replacement/empty tile.
                if (Volatile.Read(ref _disposed) != 0 || _pipeline == null)
                    return;
                StatusPanel.Visibility = Visibility.Collapsed;
                _useAlternateCodec = false;
                _alternateCodecAttempted = false;
                RaiseState(WhepPlaybackState_v3.Playing);
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private static SolidColorBrush GetRoiBrush(int index, SolidColorBrush first, SolidColorBrush second,
            SolidColorBrush third, SolidColorBrush fourth)
        {
            switch (index & 3)
            {
                case 1: return second;
                case 2: return third;
                case 3: return fourth;
                default: return first;
            }
        }

        private AiLabelMetrics GetAiLabelMetrics(string label, int surfaceWidth)
        {
            lock (_aiRendererSync)
            {
                AiLabelMetrics metrics;
                if (_aiLabelMetrics.TryGetValue(label, out metrics)) return metrics;
                using (var layout = new TextLayout(_aiTextFactory, label, _aiTextFormat,
                    Math.Max(80, surfaceWidth), 40))
                {
                    metrics = new AiLabelMetrics { Width = layout.Metrics.Width, Height = layout.Metrics.Height };
                }
                while (_aiLabelMetricsOrder.Count >= MaxAiLabelMetrics)
                    _aiLabelMetrics.Remove(_aiLabelMetricsOrder.Dequeue());
                _aiLabelMetrics[label] = metrics;
                _aiLabelMetricsOrder.Enqueue(label);
                return metrics;
            }
        }

        private async System.Threading.Tasks.Task RefreshRoiColorIndicesAsync(string cameraId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(cameraId)) return;
            try
            {
                var rois = await new LiveStreamService_v3().FetchRoisAsync(cameraId, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) return;
                lock (_roiColorSync)
                {
                    _roiColorIndices.Clear();
                    for (var index = 0; index < rois.Count; index++)
                    {
                        var roi = rois[index];
                        if (roi != null && !string.IsNullOrWhiteSpace(roi.Id))
                            _roiColorIndices[roi.Id] = index % AiRoiColors.Length;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerManager.LogDebug("Live View _v3 ROI colour lookup skipped: " + ex.Message);
            }
        }

        private int GetRoiColorIndex(string roiId)
        {
            lock (_roiColorSync)
            {
                int colorIndex;
                if (!string.IsNullOrWhiteSpace(roiId) && _roiColorIndices.TryGetValue(roiId, out colorIndex))
                    return colorIndex;

                if (!string.IsNullOrWhiteSpace(roiId))
                {
                    colorIndex = _roiColorIndices.Count % AiRoiColors.Length;
                    _roiColorIndices[roiId] = colorIndex;
                    return colorIndex;
                }
            }

            return 0;
        }

        private static bool TryMapAiBounds(AiMetadataBox_v3 item, int sourceWidth, int sourceHeight,
            float ratioX, float ratioY, out float left, out float top, out float width, out float height)
        {
            left = top = width = height = 0;
            if (item == null) return false;
            var rawLeft = item.HasPixelBounds ? item.PixelLeft : item.Left;
            var rawTop = item.HasPixelBounds ? item.PixelTop : item.Top;
            var rawWidth = item.HasPixelBounds ? item.PixelWidth : item.Width;
            var rawHeight = item.HasPixelBounds ? item.PixelHeight : item.Height;
            if (!item.HasPixelBounds && rawLeft >= 0 && rawTop >= 0 && rawWidth > 0 && rawHeight > 0 &&
                rawLeft <= 1 && rawTop <= 1 && rawWidth <= 1 && rawHeight <= 1)
            {
                rawLeft *= sourceWidth; rawTop *= sourceHeight; rawWidth *= sourceWidth; rawHeight *= sourceHeight;
            }
            left = (float)(rawLeft * ratioX); top = (float)(rawTop * ratioY);
            width = (float)(rawWidth * ratioX); height = (float)(rawHeight * ratioY);
            return width > 1 && height > 1;
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

        private void AttachBusMessagePump(Pipeline pipeline)
        {
            var messagePump = new GstBusMessagePump(pipeline.Bus);
            var enabled = false;
            var attached = false;
            try
            {
                messagePump.Bus.EnableSyncMessageEmission();
                enabled = true;
                messagePump.Bus.SyncMessage += OnSyncMessage;
                attached = true;
                if (!_busMessagePumps.TryAdd(pipeline, messagePump))
                    throw new InvalidOperationException("A GStreamer bus pump is already registered for this pipeline.");
            }
            catch
            {
                if (attached) { try { messagePump.Bus.SyncMessage -= OnSyncMessage; } catch { } }
                if (enabled) { try { messagePump.Bus.DisableSyncMessageEmission(); } catch { } }
                messagePump.Dispose();
                throw;
            }
        }

        private GstBusMessagePump DetachBusMessagePump(Pipeline pipeline)
        {
            GstBusMessagePump messagePump;
            if (!_busMessagePumps.TryRemove(pipeline, out messagePump)) return null;
            try { messagePump.Bus.SyncMessage -= OnSyncMessage; } catch { }
            try { messagePump.Bus.DisableSyncMessageEmission(); } catch { }
            return messagePump;
        }

        private void DisposePipelineInstance(Pipeline pipeline)
        {
            if (pipeline == null) return;
            var messagePump = DetachBusMessagePump(pipeline);
            try
            {
                try { if (messagePump != null) messagePump.Dispose(); }
                catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 GStreamer bus pump cleanup failed"); }
                pipeline.SetState(State.Null);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Live View _v3 GStreamer pipeline cleanup failed");
            }
            finally
            {
                ReleaseAiOverlayElement(pipeline);
                try { pipeline.Dispose(); }
                catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 GStreamer pipeline dispose failed"); }
            }
        }

        private void ReleaseAiOverlayElement(Pipeline pipeline)
        {
            Element overlay = null;
            lock (_aiRendererSync)
            {
                if (!ReferenceEquals(_aiOverlayPipeline, pipeline)) return;
                overlay = _aiOverlayElement;
                _aiOverlayElement = null;
                _aiOverlayPipeline = null;
            }
            try { overlay?.Dispose(); }
            catch (Exception ex) { LoggerManager.LogDebug("Live View _v3 AI overlay cleanup skipped: " + ex.Message); }
        }

        private IntPtr GetVideoWindowHandle()
        {
            if (_videoSurface.IsDisposed) return IntPtr.Zero;
            if (!_videoSurface.InvokeRequired) return _videoSurface.Handle;
            return (IntPtr)_videoSurface.Invoke(new Func<IntPtr>(() => _videoSurface.Handle));
        }

        private async System.Threading.Tasks.Task<IntPtr> WaitForVisibleVideoHostAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.InvokeAsync(new Action(() => { }),
                    System.Windows.Threading.DispatcherPriority.Render);

                if (!_videoSurface.IsDisposed && _videoSurface.IsHandleCreated &&
                    VideoHost.IsVisible && VideoHost.ActualWidth >= 2 && VideoHost.ActualHeight >= 2)
                {
                    var handle = GetVideoWindowHandle();
                    if (handle != IntPtr.Zero)
                        return handle;
                }

                await System.Threading.Tasks.Task.Delay(40, cancellationToken).ConfigureAwait(true);
            }

            throw new InvalidOperationException("Video surface is not ready after the window layout transition.");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            byte ignored;
            ActivePlayers.TryRemove(this, out ignored);
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            ClearAiMetadata();
            _ = System.Threading.Tasks.Task.Run(() => DisposeAfterPendingConnectionAsync());
        }

        private async System.Threading.Tasks.Task DisposeAfterPendingConnectionAsync()
        {
            try
            {
                await _connectionGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    DisposePipeline();
                    lock (_aiRendererSync)
                    {
                        _aiTextFormat?.Dispose();
                        _aiTextFormat = null;
                        _aiTextFactory?.Dispose();
                        _aiTextFactory = null;
                        _aiDrawFactory?.Dispose();
                        _aiDrawFactory = null;
                    }
                }
                finally
                {
                    _connectionGate.Release();
                }

                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _videoWindowHandle = IntPtr.Zero;
                        try { VideoHost.Dispose(); } catch (ObjectDisposedException) { }
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Live View _v3 deferred player cleanup failed");
            }
        }
    }
}
