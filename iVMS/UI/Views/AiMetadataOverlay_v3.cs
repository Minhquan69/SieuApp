using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using V3SClient.Services;

namespace V3SClient.UI.Views
{
    public sealed class AiMetadataOverlay_v3 : FrameworkElement, IDisposable
    {
        private IDisposable _subscription;
        private IList<AiMetadataBox_v3> _objects = new List<AiMetadataBox_v3>();
        private IList<RoiConfig_v3> _rois = new List<RoiConfig_v3>();
        private CancellationTokenSource _roiCancellation;
        private readonly LiveStreamService_v3 _streamService = new LiveStreamService_v3();

        public void Subscribe(string cameraId)
        {
            _subscription?.Dispose();
            _subscription = string.IsNullOrWhiteSpace(cameraId) ? null : MetadataSocketService_v3.Instance.Subscribe(cameraId, OnFrame);
            _objects = new List<AiMetadataBox_v3>();
            _roiCancellation?.Cancel();
            _roiCancellation?.Dispose();
            _roiCancellation = new CancellationTokenSource();
            if (!string.IsNullOrWhiteSpace(cameraId)) _ = LoadRoisAsync(cameraId, _roiCancellation.Token);
            InvalidateVisual();
        }

        private async System.Threading.Tasks.Task LoadRoisAsync(string cameraId, CancellationToken token)
        {
            try
            {
                var rois = await _streamService.FetchRoisAsync(cameraId, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                _ = Dispatcher.BeginInvoke(new Action(() => { _rois = rois; InvalidateVisual(); }));
            }
            catch (OperationCanceledException) { }
        }

        private void OnFrame(AiMetadataFrame_v3 frame)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _objects = frame.Objects ?? new List<AiMetadataBox_v3>();
                InvalidateVisual();
            }));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var warning = (Brush)FindResource("VmsWarningBrush_v3");
            var danger = (Brush)FindResource("VmsErrorBrush_v3");
            var info = (Brush)FindResource("VmsInfoBrush_v3");
            foreach (var roi in _rois)
            {
                if (roi.Points == null || roi.Points.Count < 2) continue;
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    var first = roi.Points[0];
                    context.BeginFigure(new Point(first[0] * ActualWidth, first[1] * ActualHeight), false, true);
                    context.PolyLineTo(System.Linq.Enumerable.Select(roi.Points, point => new Point(point[0] * ActualWidth, point[1] * ActualHeight)).Skip(1).ToList(), true, true);
                }
                drawingContext.DrawGeometry(null, new Pen(info, 1.5), geometry);
            }
            foreach (var item in _objects)
            {
                var normalized = item.Left <= 1 && item.Top <= 1 && item.Width <= 1 && item.Height <= 1;
                var left = normalized ? item.Left * ActualWidth : item.Left;
                var top = normalized ? item.Top * ActualHeight : item.Top;
                var width = normalized ? item.Width * ActualWidth : item.Width;
                var height = normalized ? item.Height * ActualHeight : item.Height;
                if (width <= 0 || height <= 0) continue;
                var brush = item.IsBlacklist ? danger : warning;
                drawingContext.DrawRectangle(null, new Pen(brush, 2), new Rect(left, top, width, height));
                var label = item.Label + (item.Confidence > 0 ? " " + item.Confidence.ToString("P0") : string.Empty);
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawingContext.DrawText(text, new Point(left, Math.Max(0, top - text.Height)));
            }
        }

        public void Dispose() { _subscription?.Dispose(); _subscription = null; _roiCancellation?.Cancel(); _roiCancellation?.Dispose(); _roiCancellation = null; _objects = new List<AiMetadataBox_v3>(); _rois = new List<RoiConfig_v3>(); }
    }
}
