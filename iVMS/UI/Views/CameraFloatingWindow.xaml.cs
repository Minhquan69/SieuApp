using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using V3SClient.libs;
using V3SClient.Services;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class CameraFloatingWindow : Window
    {
        private WhepPlayer_v3 _cameraPlayer;
        private IDisposable _metadataSubscription;
        private bool _isFullscreen;
        private bool _allowClose;
        private bool _adjustingSize;
        private double _lastWidth;
        private double _lastHeight;
        private const double VideoAspectRatio = 16.0 / 9.0;
        private const double HorizontalChrome = 2.0;
        private const double VerticalChrome = 31.0;

        public CameraFloatingWindow()
        {
            InitializeComponent();
            _lastWidth = Width;
            _lastHeight = Height;
            StateChanged += (sender, args) => Dispatcher.BeginInvoke(
                new Action(FitCameraToContainer), DispatcherPriority.ContextIdle);
        }

        /// <summary>Displays and immediately starts the selected live camera.</summary>
        public void ShowCamera(models.Camera camera, Window ownerWindow)
        {
            if (camera == null) return;

            StopCamera();
            txtCamName.Text = camera.long_Name ?? camera.name;
            _cameraPlayer = new WhepPlayer_v3
            {
                Camera = camera,
                // The map is a single-camera presentation, therefore it uses
                // the same main-stream selection as fullscreen Live View.
                SelectedStream = LiveViewModel_v3.SelectFullscreenStream(camera),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AiOverlayEnabled = HasAiStream(camera)
            };
            SubscribeAiMetadata(camera, _cameraPlayer.SelectedStream);
            gridCameraView.Children.Add(_cameraPlayer);

            try { Owner = ownerWindow; } catch { }

            WindowState = WindowState.Normal;
            _isFullscreen = false;
            SizeAndCenterToOwner(ownerWindow);

            if (!IsVisible) Show();
            Activate();

            // WindowsFormsHost creates its native handle during Loaded.  Connecting
            // at this priority guarantees GStreamer receives that ready handle.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                var player = _cameraPlayer;
                if (player != null) await player.ReconnectAsync();
            }),
                DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(FitCameraToContainer), DispatcherPriority.Render);
        }

        private static bool HasAiStream(models.Camera camera)
        {
            return camera != null &&
                (camera.HasAIStream ||
                 string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) ||
                 (camera.Streams != null && camera.Streams.Any(stream => stream != null && stream.IsAiMode == true)));
        }

        public void StopCamera()
        {
            _metadataSubscription?.Dispose();
            _metadataSubscription = null;
            if (_cameraPlayer == null) return;
            _cameraPlayer.Dispose();
            gridCameraView.Children.Clear();
            _cameraPlayer = null;
        }

        private void SubscribeAiMetadata(models.Camera camera, CameraStreamInfo stream)
        {
            // Bounding boxes are read from SEI metadata in the RTSP pipeline by
            // RtspPlayer.  Floating playback must not open a Kafka/WebSocket
            // metadata subscription, otherwise it races the stream's own frames.
            _metadataSubscription?.Dispose();
            _metadataSubscription = null;
            if (_cameraPlayer == null) return;
            _cameraPlayer.AiOverlayEnabled = HasAiStream(camera);
            _cameraPlayer.ClearAiMetadata();
        }

        private sealed class CompositeSubscription : IDisposable
        {
            private IDisposable _first;
            private IDisposable _second;

            public CompositeSubscription(IDisposable first, IDisposable second)
            {
                _first = first;
                _second = second;
            }

            public void Dispose()
            {
                var first = System.Threading.Interlocked.Exchange(ref _first, null);
                var second = System.Threading.Interlocked.Exchange(ref _second, null);
                first?.Dispose();
                second?.Dispose();
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal)
                DragMove();
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            _isFullscreen = !_isFullscreen;
            WindowState = _isFullscreen ? WindowState.Maximized : WindowState.Normal;
            Dispatcher.BeginInvoke(new Action(FitCameraToContainer), DispatcherPriority.ContextIdle);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            Hide();
        }

        private void SizeAndCenterToOwner(Window ownerWindow)
        {
            // The map can live inside a nested Shell layout whose logical bounds
            // are not the same as the visible window.  Use the desktop work area
            // so the floating camera always opens at the visual screen centre.
            var bounds = SystemParameters.WorkArea;

            var maxWidth = Math.Max(MinWidth, bounds.Width * 0.8);
            var maxHeight = Math.Max(MinHeight, bounds.Height * 0.8);
            var width = Math.Min(maxWidth, (maxHeight - VerticalChrome) * VideoAspectRatio + HorizontalChrome);
            width = Math.Max(MinWidth, width);
            var height = (width - HorizontalChrome) / VideoAspectRatio + VerticalChrome;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = (height - VerticalChrome) * VideoAspectRatio + HorizontalChrome;
            }

            _adjustingSize = true;
            Width = width;
            Height = height;
            _adjustingSize = false;
            _lastWidth = Width;
            _lastHeight = Height;
            Left = bounds.Left + (bounds.Width - Width) / 2;
            Top = bounds.Top + (bounds.Height - Height) / 2;
        }

        private void CameraFloatingWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_adjustingSize || WindowState != WindowState.Normal) return;

            var widthDelta = Math.Abs(ActualWidth - _lastWidth);
            var heightDelta = Math.Abs(ActualHeight - _lastHeight);
            _adjustingSize = true;
            if (widthDelta >= heightDelta * VideoAspectRatio)
                Height = (ActualWidth - HorizontalChrome) / VideoAspectRatio + VerticalChrome;
            else
                Width = (ActualHeight - VerticalChrome) * VideoAspectRatio + HorizontalChrome;
            _adjustingSize = false;
            _lastWidth = ActualWidth;
            _lastHeight = ActualHeight;
            Dispatcher.BeginInvoke(new Action(FitCameraToContainer), DispatcherPriority.Render);
        }

        private void CameraContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitCameraToContainer();
        }

        private void FitCameraToContainer()
        {
            if (_cameraPlayer == null || gridCameraView.ActualWidth <= 0 || gridCameraView.ActualHeight <= 0)
                return;

            var width = Math.Min(gridCameraView.ActualWidth,
                gridCameraView.ActualHeight * VideoAspectRatio);
            var height = width / VideoAspectRatio;
            _cameraPlayer.HorizontalAlignment = HorizontalAlignment.Center;
            _cameraPlayer.VerticalAlignment = VerticalAlignment.Center;
            _cameraPlayer.Width = Math.Max(1, width);
            _cameraPlayer.Height = Math.Max(1, height);
        }

        public void ForceClose()
        {
            _allowClose = true;
            StopCamera();
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            StopCamera();
            Hide();
        }
    }
}
