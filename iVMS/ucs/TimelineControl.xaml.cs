using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using V3SClient.libs;

namespace V3SClient.ucs
{
    public partial class TimelineControl : UserControl
    {
        private System.DateTime _startTime;
        private System.DateTime _endTime;
        private double _totalSeconds;
        private bool _isDragging = false; // For seeking
        private bool _isSelecting = false; // For range selection
        private double _selectionStartX = 0;
        private int _totalLanes = 0;

        private bool _isSettingStart = false;
        private bool _isSettingEnd = false;
        private bool _isManualSelection = false;
        
        private double _timelineWidth = 0;
        private double LANE_HEIGHT = 8; // Default Compact
        private double LANE_MARGIN = 4;
        
        public bool IsExpanded { get; set; } = false;

        private class LaneData
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string SessionId { get; set; }
            public List<ApiManager.PlaybackVideoInfo> Segments { get; set; }
        }
        private List<LaneData> _lanes = new List<LaneData>();
        
        public event EventHandler<System.DateTime> SeekRequested;
        public event EventHandler<(System.DateTime start, System.DateTime end)> SelectionChanged;

        public System.DateTime? SelectionStart { get; private set; }
        public System.DateTime? SelectionEnd { get; private set; }

        public event EventHandler DownloadRequested;

        public TimelineControl()
        {
            InitializeComponent();
            this.SizeChanged += TimelineControl_SizeChanged;
        }

        private void TimelineControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_totalSeconds > 0)
            {
                RenderTimeline();
            }
        }

        public void SetTimeRange(System.DateTime start, System.DateTime end)
        {
            _startTime = start;
            _endTime = end;
            _totalSeconds = (_endTime - _startTime).TotalSeconds;
            
            RenderTimeline();
        }

        public void Clear()
        {
            canvasTimeline.Children.Clear();
            canvasGridLines.Children.Clear();
            canvasTimeLabels.Children.Clear();
            pnlCameraHeaders.Children.Clear();
            _lanes.Clear();
            _totalLanes = 0;
        }

        private void BtnExpand_Changed(object sender, RoutedEventArgs e)
        {
            IsExpanded = btnExpand.IsChecked == true;
            btnExpand.Content = IsExpanded ? "-" : "+";
            
            // Adjust Sizing
            if (IsExpanded)
            {
                LANE_HEIGHT = 32;
                LANE_MARGIN = 8;
            }
            else
            {
                LANE_HEIGHT = 8;
                LANE_MARGIN = 4;
            }

            RefreshTimeline();
        }

        private void RefreshTimeline()
        {
            var savedLanes = _lanes.ToList();
            var savedStartTime = _startTime;
            var savedEndTime = _endTime;

            Clear();
            SetTimeRange(savedStartTime, savedEndTime);

            foreach (var lane in savedLanes)
            {
                AddCameraLane(lane.DeviceId, lane.DeviceName, lane.Segments, lane.SessionId);
            }
        }

        private void RenderTimeline()
        {
            double minWidth = (_totalSeconds / 60) * 2; 
            _timelineWidth = Math.Max(scrollTimeline.ViewportWidth, minWidth);
            
            if (_timelineWidth == 0) _timelineWidth = this.ActualWidth - 150; 
            if (_timelineWidth < 100) _timelineWidth = 100;

            gridTimelineContainer.Width = _timelineWidth;
            canvasTimeLabels.Width = _timelineWidth;

            RenderTimeLabelsAndGrid();
        }

        public void AddCameraLane(string deviceId, string deviceName, List<ApiManager.PlaybackVideoInfo> segments, string sessionId = null)
        {
            // Store for refresh
            if (!_lanes.Any(l => l.DeviceId == deviceId))
            {
                _lanes.Add(new LaneData { DeviceId = deviceId, DeviceName = deviceName, Segments = segments, SessionId = sessionId });
            }

            int laneIndex = _totalLanes++;
            
            Border headerBorder = new Border
            {
                Height = LANE_HEIGHT + LANE_MARGIN,
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Top
            };

            StackPanel headerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            
            TextBlock headerTxt = new TextBlock
            {
                Text = string.IsNullOrEmpty(deviceName) ? deviceId : deviceName,
                Foreground = Brushes.White,
                FontWeight = IsExpanded ? FontWeights.Bold : FontWeights.Normal,
                FontSize = IsExpanded ? 11 : 9,
                ToolTip = deviceId
            };
            headerStack.Children.Add(headerTxt);

            if (IsExpanded && !string.IsNullOrEmpty(sessionId))
            {
                TextBlock sessionTxt = new TextBlock
                {
                    Text = $"Sess: {sessionId}",
                    Foreground = Brushes.Gray,
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                headerStack.Children.Add(sessionTxt);
            }

            headerBorder.Child = headerStack;
            pnlCameraHeaders.Children.Add(headerBorder);

            if (laneIndex % 2 == 0)
            {
                Rectangle bgLane = new Rectangle
                {
                    Width = 10000, 
                    Height = LANE_HEIGHT + LANE_MARGIN,
                    Fill = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                };
                Canvas.SetTop(bgLane, laneIndex * (LANE_HEIGHT + LANE_MARGIN));
                canvasGridLines.Children.Add(bgLane);
            }

            double laneY = laneIndex * (LANE_HEIGHT + LANE_MARGIN) + (LANE_MARGIN / 2);
            foreach (var seg in segments)
            {
                double startPos = TimeToX(seg.StartTime);
                double endPos = TimeToX(seg.EndTime);

                Rectangle rect = new Rectangle
                {
                    Width = Math.Max(2, endPos - startPos),
                    Height = LANE_HEIGHT,
                    Fill = new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                    Opacity = 0.8,
                    RadiusX = 2,
                    RadiusY = 2,
                    ToolTip = $"{deviceName}: {seg.StartTime:HH:mm:ss} - {seg.EndTime:HH:mm:ss}"
                };

                Canvas.SetLeft(rect, startPos);
                Canvas.SetTop(rect, laneY);
                canvasTimeline.Children.Add(rect);
            }
            
            gridTimelineContainer.Height = Math.Max(scrollTimeline.ViewportHeight, _totalLanes * (LANE_HEIGHT + LANE_MARGIN));
        }

        public void AddVideoSegments(string deviceId, List<ApiManager.PlaybackVideoInfo> segments, int laneIndex)
        {
            // Backward compatibility wrapper
            AddCameraLane(deviceId, deviceId, segments);
        }

        public void AddEventMarker(System.DateTime time, string label, Color color)
        {
            if (time < _startTime || time > _endTime) return;

            double x = TimeToX(time);
            
            Line eventLine = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = 10000, 
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Opacity = 0.5
            };
            canvasGridLines.Children.Add(eventLine);

            Ellipse marker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(color),
                ToolTip = $"{label} at {time:HH:mm:ss}"
            };
            marker.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = 5 };

            Canvas.SetLeft(marker, x - 5);
            Canvas.SetTop(marker, 2);
            canvasTimeline.Children.Add(marker);
        }

        public void UpdatePlayhead(System.DateTime currentTime)
        {
            if (currentTime < _startTime || currentTime > _endTime) return;
            double x = TimeToX(currentTime);
            rectPlayhead.Margin = new Thickness(x, 0, 0, 0);
        }

        private double TimeToX(System.DateTime time)
        {
            if (_totalSeconds <= 0 || _timelineWidth <= 0) return 0;
            double offset = (time - _startTime).TotalSeconds;
            return Math.Max(0, (offset / _totalSeconds) * _timelineWidth);
        }

        private System.DateTime XToTime(double x)
        {
            if (_timelineWidth <= 0) return _startTime;
            double ratio = Math.Max(0, Math.Min(1, x / _timelineWidth));
            return _startTime.AddSeconds(ratio * _totalSeconds);
        }

        private void RenderTimeLabelsAndGrid()
        {
            canvasTimeLabels.Children.Clear();
            canvasGridLines.Children.Clear();

            if (_totalSeconds <= 0 || _timelineWidth <= 0) return;

            int numIntervals = Math.Max(2, (int)(_timelineWidth / 100));
            double secondsPerInterval = _totalSeconds / numIntervals;

            for (int i = 0; i <= numIntervals; i++)
            {
                System.DateTime time = _startTime.AddSeconds(secondsPerInterval * i);
                double x = (double)i / numIntervals * _timelineWidth;

                Line majorLine = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 10000,
                    Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    StrokeThickness = 1
                };
                canvasGridLines.Children.Add(majorLine);

                TextBlock txt = new TextBlock
                {
                    Text = time.ToString("HH:mm:ss"),
                    Foreground = Brushes.Gray,
                    FontSize = 10
                };
                txt.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                Canvas.SetLeft(txt, x - (txt.DesiredSize.Width / 2));
                Canvas.SetTop(txt, 10);
                canvasTimeLabels.Children.Add(txt);
                
                Line tick = new Line
                {
                    X1 = x,
                    Y1 = 25,
                    X2 = x,
                    Y2 = 30,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1
                };
                canvasTimeLabels.Children.Add(tick);
            }
        }

        private void ScrollTimeline_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0)
                scrollHeaders.ScrollToVerticalOffset(e.VerticalOffset);
            if (e.HorizontalChange != 0)
                scrollRuler.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        private void CanvasTimeline_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(canvasTimeline);
            if (e.ChangedButton == MouseButton.Right) return;

            // Manual Selection Mode
            if (_isSettingStart)
            {
                SelectionStart = XToTime(pos.X);
                UpdateManualMarkers();
                _isSettingStart = false;
                SelectionChanged?.Invoke(this, (SelectionStart.Value, SelectionEnd ?? SelectionStart.Value));
                return;
            }

            if (_isSettingEnd)
            {
                SelectionEnd = XToTime(pos.X);
                UpdateManualMarkers();
                _isSettingEnd = false;
                SelectionChanged?.Invoke(this, (SelectionStart ?? SelectionEnd.Value, SelectionEnd.Value));
                return;
            }

            // If Shift is pressed, start range selection
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                _isSelecting = true;
                _selectionStartX = pos.X;
                rectSelection.Visibility = Visibility.Visible;
                Canvas.SetLeft(rectSelection, _selectionStartX);
                rectSelection.Width = 0;
                canvasTimeline.CaptureMouse();
            }
            else
            {
                _isDragging = true;
                canvasTimeline.CaptureMouse();
                HandleSeek(pos.X);
                
                // Clear selection if not shifting
                ClearSelection();
            }
        }

        private void MenuSetStart_Click(object sender, RoutedEventArgs e)
        {
            _isSettingStart = true;
            _isSettingEnd = false;
        }

        private void MenuSetEnd_Click(object sender, RoutedEventArgs e)
        {
            _isSettingEnd = true;
            _isSettingStart = false;
        }

        private void MenuDownload_Click(object sender, RoutedEventArgs e)
        {
            if (SelectionStart != null && SelectionEnd != null)
            {
                DownloadRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MessageBox.Show("Vui lòng chọn cả điểm bắt đầu và kết thúc trước.");
            }
        }

        private void UpdateManualMarkers()
        {
            if (SelectionStart.HasValue)
            {
                double x = TimeToX(SelectionStart.Value);
                txtStartMarker.Visibility = Visibility.Visible;
                Canvas.SetLeft(txtStartMarker, x); // '[' vertical bar is on the left
                Canvas.SetTop(txtStartMarker, 0);

                txtSelectionStart.Text = SelectionStart.Value.ToString("HH:mm:ss");
                borderStart.Visibility = Visibility.Visible;
                Canvas.SetLeft(borderStart, x - 25); // Approximate center
                Canvas.SetTop(borderStart, 30);
            }
            else
            {
                txtStartMarker.Visibility = Visibility.Collapsed;
                borderStart.Visibility = Visibility.Collapsed;
            }

            if (SelectionEnd.HasValue)
            {
                double x = TimeToX(SelectionEnd.Value);
                txtEndMarker.Visibility = Visibility.Visible;
                // ']' vertical bar is on the right. 
                // We offset by a small amount to make it look right.
                Canvas.SetLeft(txtEndMarker, x - 15); 
                Canvas.SetTop(txtEndMarker, 0);

                txtSelectionEnd.Text = SelectionEnd.Value.ToString("HH:mm:ss");
                borderEnd.Visibility = Visibility.Visible;
                Canvas.SetLeft(borderEnd, x - 25);
                Canvas.SetTop(borderEnd, 30);
            }
            else
            {
                txtEndMarker.Visibility = Visibility.Collapsed;
                borderEnd.Visibility = Visibility.Collapsed;
            }

            // Also update the blue rectangle overlay if both exist
            if (SelectionStart.HasValue && SelectionEnd.HasValue)
            {
                double x1 = TimeToX(SelectionStart.Value);
                double x2 = TimeToX(SelectionEnd.Value);
                double left = Math.Min(x1, x2);
                double width = Math.Abs(x1 - x2);

                rectSelection.Visibility = Visibility.Visible;
                Canvas.SetLeft(rectSelection, left);
                rectSelection.Width = width;
            }
            else
            {
                rectSelection.Visibility = Visibility.Collapsed;
            }
        }

        private void CanvasTimeline_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(canvasTimeline);
            if (_isSelecting)
            {
                double x = pos.X;
                double left = Math.Min(_selectionStartX, x);
                double width = Math.Abs(_selectionStartX - x);
                double right = left + width;

                Canvas.SetLeft(rectSelection, Math.Max(0, left));
                rectSelection.Width = Math.Min(width, _timelineWidth - left);

                // Update Labels
                DateTime startTime = XToTime(left);
                DateTime endTime = XToTime(right);

                txtSelectionStart.Text = startTime.ToString("HH:mm:ss");
                txtSelectionEnd.Text = endTime.ToString("HH:mm:ss");

                Canvas.SetLeft(borderStart, left - 25);
                Canvas.SetTop(borderStart, 30);
                borderStart.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF4CAF50");

                Canvas.SetLeft(borderEnd, right - 25);
                Canvas.SetTop(borderEnd, 30);
                borderEnd.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFF44336");

                borderStart.Visibility = Visibility.Visible;
                borderEnd.Visibility = Visibility.Visible;
            }
            else if (_isDragging)
            {
                HandleSeek(pos.X);
            }
        }

        private void CanvasTimeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                canvasTimeline.ReleaseMouseCapture();
                
                double left = Canvas.GetLeft(rectSelection);
                double right = left + rectSelection.Width;
                
                SelectionStart = XToTime(left);
                SelectionEnd = XToTime(right);
                
                SelectionChanged?.Invoke(this, (SelectionStart.Value, SelectionEnd.Value));
            }
            else if (_isDragging)
            {
                _isDragging = false;
                canvasTimeline.ReleaseMouseCapture();
                HandleSeek(e.GetPosition(canvasTimeline).X);
            }
        }

        private void CanvasTimeline_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                // We keep selection even if mouse leaves while dragging? 
                // Usually it's better to finish it on MouseUp.
            }
            else if (_isDragging)
            {
                _isDragging = false;
                canvasTimeline.ReleaseMouseCapture();
            }
        }

        public void ClearSelection()
        {
            SelectionStart = null;
            SelectionEnd = null;
            rectSelection.Visibility = Visibility.Collapsed;
            rectSelection.Width = 0;
            borderStart.Visibility = Visibility.Collapsed;
            borderEnd.Visibility = Visibility.Collapsed;
            txtStartMarker.Visibility = Visibility.Collapsed;
            txtEndMarker.Visibility = Visibility.Collapsed;
        }

        private void HandleSeek(double x)
        {
            System.DateTime seekTime = XToTime(Math.Max(0, Math.Min(x, _timelineWidth)));
            UpdatePlayhead(seekTime);
            SeekRequested?.Invoke(this, seekTime);
        }
    }
}
