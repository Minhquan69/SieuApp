using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace V3SClient.ucs
{
    /// <summary>
    /// Custom donut chart control that renders a circular progress indicator.
    /// Usage: <ucs:DonutChartControl Value="86.2" />
    /// </summary>
    public class DonutChartControl : Control
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(DonutChartControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(DonutChartControl),
                new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackColorProperty =
            DependencyProperty.Register(nameof(TrackColor), typeof(Color), typeof(DonutChartControl),
                new FrameworkPropertyMetadata(Color.FromArgb(0xFF, 0x2A, 0x23, 0x1E), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillColorProperty =
            DependencyProperty.Register(nameof(FillColor), typeof(Color), typeof(DonutChartControl),
                new FrameworkPropertyMetadata(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowLabelProperty =
            DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(DonutChartControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Value in percentage (0-100)</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public Color TrackColor
        {
            get => (Color)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        public Color FillColor
        {
            get => (Color)GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }

        public bool ShowLabel
        {
            get => (bool)GetValue(ShowLabelProperty);
            set => SetValue(ShowLabelProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;

            double radius = (size - StrokeThickness) / 2;
            Point center = new Point(ActualWidth / 2, ActualHeight / 2);
            double clampedValue = Math.Max(0, Math.Min(100, Value));

            // Draw track (background circle)
            var trackPen = new Pen(new SolidColorBrush(TrackColor), StrokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            // Draw fill arc
            if (clampedValue > 0)
            {
                double angle = clampedValue / 100.0 * 360.0;
                bool isLargeArc = angle > 180;

                double startAngle = -90; // Start from top
                double endAngle = startAngle + angle;

                double startRad = startAngle * Math.PI / 180;
                double endRad = endAngle * Math.PI / 180;

                Point startPoint = new Point(
                    center.X + radius * Math.Cos(startRad),
                    center.Y + radius * Math.Sin(startRad));
                Point endPoint = new Point(
                    center.X + radius * Math.Cos(endRad),
                    center.Y + radius * Math.Sin(endRad));

                var fillPen = new Pen(new SolidColorBrush(FillColor), StrokeThickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };

                if (clampedValue >= 99.9)
                {
                    // Full circle
                    dc.DrawEllipse(null, fillPen, center, radius, radius);
                }
                else
                {
                    var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
                    figure.Segments.Add(new ArcSegment(
                        endPoint,
                        new Size(radius, radius),
                        0,
                        isLargeArc,
                        SweepDirection.Clockwise,
                        true));

                    var geometry = new PathGeometry();
                    geometry.Figures.Add(figure);
                    dc.DrawGeometry(null, fillPen, geometry);
                }
            }

            // Draw center label
            if (ShowLabel)
            {
                var formattedValue = new FormattedText(
                    $"{clampedValue:F1}%",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    size * 0.18,
                    new SolidColorBrush(Color.FromRgb(0xF3, 0xEE, 0xE9)),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(formattedValue,
                    new Point(center.X - formattedValue.Width / 2, center.Y - formattedValue.Height / 2));
            }
        }
    }
}
