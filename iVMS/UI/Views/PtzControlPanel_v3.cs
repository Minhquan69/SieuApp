using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MahApps.Metro.IconPacks;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    public sealed class PtzControlPanel_v3 : UserControl
    {
        private const double PtzMovementSliceSeconds = 5.0;
        private const int PtzRenewalIntervalMilliseconds = 2500;
        private readonly string _cameraId;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private CancellationTokenSource _movementCancel = new CancellationTokenSource();
        private readonly Slider _pan = SliderOf(6), _tilt = SliderOf(5), _zoom = SliderOf(4);
        private readonly ComboBox _streams = new ComboBox { Width = 70, Height = 30, Margin = new Thickness(8, 0, 6, 0), MaxDropDownHeight = 140 };
        private readonly ComboBox _presets = new ComboBox { Height = 36, MinWidth = 170, Margin = new Thickness(0, 8, 8, 0) };
        private int _session; private bool _pressed; private Button _active;
        private bool _requestInFlight;
        public event EventHandler CloseRequested; public event Action<string> StreamChanged; public event Action<bool> ConnectionChanged;

        public PtzControlPanel_v3(string cameraId, IEnumerable<string> streams = null)
        {
            _cameraId = cameraId ?? string.Empty; Background = Brush("#071724"); Configure(_streams); Configure(_presets);
            foreach (var s in (streams ?? new[] { "main", "sub1", "sub2" }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)) _streams.Items.Add(s);
            if (_streams.Items.Count == 0) _streams.Items.Add("main"); _streams.SelectedIndex = 0;
            _presets.DisplayMemberPath = "Name"; _presets.Items.Add(EmptyPreset()); _presets.SelectedIndex = 0;
            _streams.SelectionChanged += (s, e) => { if (_streams.SelectedItem != null) StreamChanged?.Invoke(_streams.SelectedItem.ToString()); };
            PreviewMouseLeftButtonUp += (s, e) => StopActive(); PreviewMouseMove += (s, e) => { if (_pressed && Mouse.LeftButton != MouseButtonState.Pressed) StopActive(); }; Mouse.AddMouseUpHandler(this, (s, e) => StopActive());
            Loaded += async (s, e) => await LoadPresets(); Unloaded += (s, e) => { _session++; StopActive(); _cancel.Cancel(); }; Content = BuildModern();
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(14, 10, 14, 14) }; for (var i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid { Margin = new Thickness(0, 0, 0, 22) }; header.ColumnDefinitions.Add(new ColumnDefinition()); for (var i = 0; i < 3; i++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; title.Children.Add(new PackIconMaterial { Kind = PackIconMaterialKind.Crosshairs, Width = 15, Height = 15, Foreground = Brush("#16B9FF"), Margin = new Thickness(0, 0, 7, 0) }); title.Children.Add(new TextBlock { Text = "PTZ", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold }); title.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Brush("#20C978"), Margin = new Thickness(8, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }); title.Children.Add(new TextBlock { Text = _cameraId, Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = _cameraId }); header.Children.Add(title);
            Grid.SetColumn(_streams, 1); header.Children.Add(_streams); var connected = true; var link = Header("plug", "Ngắt kết nối"); link.Click += (s, e) => { connected = !connected; link.Content = HeaderIcon(connected ? PackIconMaterialKind.PowerPlug : PackIconMaterialKind.PowerPlugOff); link.ToolTip = connected ? "Ngắt kết nối" : "Kết nối"; ConnectionChanged?.Invoke(connected); }; Grid.SetColumn(link, 2); header.Children.Add(link); var close = Header("×", "Đóng PTZ"); close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty); Grid.SetColumn(close, 3); header.Children.Add(close); Grid.SetRow(header, 0); root.Children.Add(header);
            var controls = new Grid { Margin = new Thickness(0, 0, 0, 18) }; controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); var pad = new Grid { Width = 142, Height = 142, HorizontalAlignment = HorizontalAlignment.Center }; for (var i = 0; i < 3; i++) { pad.RowDefinitions.Add(new RowDefinition()); pad.ColumnDefinitions.Add(new ColumnDefinition()); } Direction(pad, "▲", 0, 1, 0, 1, 0); Direction(pad, "◀", 1, 0, -1, 0, 0); Direction(pad, "●", 1, 1, 0, 0, 0); Direction(pad, "▶", 1, 2, 1, 0, 0); Direction(pad, "▼", 2, 1, 0, -1, 0); controls.Children.Add(pad); var zoom = new StackPanel(); var plus = Button("⌕+", "Phóng to"); plus.Click += async (s, e) => await Move(0, 0, 1); var minus = Button("⌕−", "Thu nhỏ"); minus.Click += async (s, e) => await Move(0, 0, -1); zoom.Children.Add(plus); zoom.Children.Add(minus); Grid.SetColumn(zoom, 1); controls.Children.Add(zoom); Grid.SetRow(controls, 1); root.Children.Add(controls);
            var speeds = new StackPanel(); Speed(speeds, "Tốc độ Pan", _pan); Speed(speeds, "Tốc độ Tilt", _tilt); Speed(speeds, "Tốc độ Zoom", _zoom); Grid.SetRow(speeds, 2); root.Children.Add(speeds);
            var preset = new StackPanel { Margin = new Thickness(0, 18, 0, 0) }; var ph = new DockPanel(); ph.Children.Add(new TextBlock { Text = "Vị trí đặt trước", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold }); var refresh = Button("↻", "Tải lại vị trí"); refresh.Width = 30; refresh.Height = 30; refresh.Click += async (s, e) => await LoadPresets(); DockPanel.SetDock(refresh, Dock.Right); ph.Children.Add(refresh); preset.Children.Add(ph); var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(_presets); var go = Button("Đi tới", "Đi tới vị trí"); go.Width = 58; go.Height = 36; go.Click += async (s, e) => { var p = _presets.SelectedItem as ApiManager.CameraPtzPreset; if (p != null) await ApiManager.Instance.GotoCameraPtzPresetAsync(_cameraId, p.Token, _cancel.Token); }; row.Children.Add(go); preset.Children.Add(row); Grid.SetRow(preset, 3); root.Children.Add(preset); return root;
        }
        private UIElement BuildModern()
        {
            MinWidth = 302;
            var root = new Grid { Margin = new Thickness(20, 12, 20, 18) };
            for (var i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 3; i++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new PackIconMaterial { Kind = PackIconMaterialKind.Crosshairs, Width = 15, Height = 15, Foreground = Brush("#2388FF"), Margin = new Thickness(0, 0, 6, 0) });
            title.Children.Add(new TextBlock { Text = "PTZ", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold });
            title.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brush("#20C978"), Margin = new Thickness(7, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = _cameraId, Foreground = Brush("#C9D8E8"), FontSize = 11, FontWeight = FontWeights.SemiBold, MaxWidth = 75, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = _cameraId });
            header.Children.Add(title);
            Grid.SetColumn(_streams, 1); header.Children.Add(_streams);
            var connected = true; var link = Header("plug", "Ngắt kết nối"); link.Click += (s, e) => { connected = !connected; link.Content = HeaderIcon(connected ? PackIconMaterialKind.PowerPlug : PackIconMaterialKind.PowerPlugOff); link.ToolTip = connected ? "Ngắt kết nối" : "Kết nối"; ConnectionChanged?.Invoke(connected); }; Grid.SetColumn(link, 2); header.Children.Add(link);
            var close = Header("×", "Đóng PTZ"); close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty); Grid.SetColumn(close, 3); header.Children.Add(close);
            root.Children.Add(header);

            var controls = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition());
            controls.Children.Add(BuildDirectionPad());
            var right = new StackPanel();
            var zoom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 10) };
            var plus = ZoomButton(PackIconMaterialKind.MagnifyPlusOutline, "Phóng to"); BindHold(plus, 0, 0, 1); zoom.Children.Add(plus);
            var minus = ZoomButton(PackIconMaterialKind.MagnifyMinusOutline, "Thu nhỏ"); minus.Margin = new Thickness(7, 0, 0, 0); BindHold(minus, 0, 0, -1); zoom.Children.Add(minus);
            right.Children.Add(zoom); SpeedModern(right, "Tốc độ Pan", _pan); SpeedModern(right, "Tốc độ Tilt", _tilt); SpeedModern(right, "Tốc độ Zoom", _zoom);
            Grid.SetColumn(right, 2); controls.Children.Add(right); Grid.SetRow(controls, 1); root.Children.Add(controls);

            var preset = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            preset.Children.Add(new Border { Height = 1, Background = Brush("#143047"), Margin = new Thickness(0, 0, 0, 13) });
            var presetHeader = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            presetHeader.ColumnDefinitions.Add(new ColumnDefinition()); presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            presetHeader.Children.Add(new TextBlock { Text = "Vị trí đặt trước", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var refresh = StyledButton(string.Empty, "Tải lại vị trí", 28, 28, 14, "#0A2641"); refresh.Content = new PackIconMaterial { Kind = PackIconMaterialKind.Refresh, Width = 15, Height = 15, Foreground = Brushes.White }; refresh.Click += async (s, e) => await LoadPresets(); Grid.SetColumn(refresh, 1); presetHeader.Children.Add(refresh); preset.Children.Add(presetHeader);
            var presetRow = new Grid { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            presetRow.ColumnDefinitions.Add(new ColumnDefinition());
            presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            _presets.Width = double.NaN; _presets.Margin = new Thickness(0);
            Grid.SetColumn(_presets, 0); presetRow.Children.Add(_presets);
            var go = StyledButton("Đi tới", "Đi tới vị trí", 58, 36, 12, "#10273A"); go.Style = RoundStyle("#10273A", "#245B9B", 3); go.Margin = new Thickness(8, 0, 0, 0); go.VerticalAlignment = VerticalAlignment.Center; go.HorizontalAlignment = HorizontalAlignment.Left; go.Width = 50; go.BorderBrush = Brush("#245B9B"); go.BorderThickness = new Thickness(1); go.Opacity = 0.45; Panel.SetZIndex(go, 2); go.Click += async (s, e) => { var p = _presets.SelectedItem as ApiManager.CameraPtzPreset; if (p == null || string.IsNullOrWhiteSpace(p.Token)) return; await ApiManager.Instance.GotoCameraPtzPresetAsync(_cameraId, p.Token, _cancel.Token); }; _presets.SelectionChanged += (s, e) => { var p = _presets.SelectedItem as ApiManager.CameraPtzPreset; go.Opacity = p != null && !string.IsNullOrWhiteSpace(p.Token) ? 1.0 : 0.45; }; Grid.SetColumn(go, 1); presetRow.Children.Add(go); preset.Children.Add(presetRow);
            Grid.SetRow(preset, 3); root.Children.Add(preset);
            return root;
        }

        private static void SpeedModern(Panel panel, string label, Slider slider)
        {
            var heading = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11 });
            var value = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold }; value.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Value") { Source = slider, StringFormat = "{0:0}" }); Grid.SetColumn(value, 1); heading.Children.Add(value);
            slider.Margin = new Thickness(0, -2, 0, 1); slider.Height = 17; slider.Foreground = Brush("#2B7FFF"); panel.Children.Add(heading); panel.Children.Add(slider);
        }

        private Canvas BuildDirectionPad()
        {
            var pad = new Canvas { Width = 138, Height = 138, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            var fill = Brush("#5E7883"); var border = Brush("#344A53");
            AddPadSector(pad, -135, -45, PackIconMaterialKind.ChevronUp, 69, 18);
            AddPadSector(pad, 45, 135, PackIconMaterialKind.ChevronDown, 69, 120);
            AddPadSector(pad, 135, 225, PackIconMaterialKind.ChevronLeft, 18, 69);
            AddPadSector(pad, -45, 45, PackIconMaterialKind.ChevronRight, 120, 69);
            var center = new Ellipse { Width = 68, Height = 68, Fill = Brush("#9BACB5"), Stroke = border, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(center, 35); Canvas.SetTop(center, 35); pad.Children.Add(center);
            pad.MouseMove += (s, e) => SetPadHighlight(pad, e.GetPosition(pad), _pressed);
            pad.MouseLeave += (s, e) => SetPadHighlight(pad, new Point(-1, -1), false);
            pad.MouseLeftButtonDown += (s, e) => { var p = e.GetPosition(pad); var dx = p.X - 69; var dy = p.Y - 69; if (Math.Abs(dx) < 34 && Math.Abs(dy) < 34) return; var pan = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : -1) : 0; var tilt = Math.Abs(dx) > Math.Abs(dy) ? 0 : (dy > 0 ? -1 : 1); pad.CaptureMouse(); SetPadHighlight(pad, p, true); BeginMove(pan, tilt, 0, null); e.Handled = true; };
            pad.MouseLeftButtonUp += (s, e) => { StopCurrentMove(); if (pad.IsMouseCaptured) pad.ReleaseMouseCapture(); SetPadHighlight(pad, e.GetPosition(pad), false); e.Handled = true; };
            pad.LostMouseCapture += (s, e) => StopCurrentMove();
            return pad;
        }

        private void AddPadSector(Canvas pad, double startAngle, double endAngle, PackIconMaterialKind arrow, double left, double top)
        {
            var shape = new Path { Data = CreateSectorGeometry(startAngle, endAngle), Fill = Brush("#5E7883"), Stroke = Brush("#344A53"), StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(shape, 0); Canvas.SetTop(shape, 0); pad.Children.Add(shape);
            var label = new PackIconMaterial { Kind = arrow, Foreground = Brushes.White, Width = 20, Height = 20, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetLeft(label, left - 10); Canvas.SetTop(label, top - 10); Panel.SetZIndex(label, 2); pad.Children.Add(label);
        }

        private static void SetPadHighlight(Canvas pad, Point point, bool pressed)
        {
            var dx = point.X - 69; var dy = point.Y - 69;
            var outside = point.X < 0 || point.Y < 0 || point.X > 138 || point.Y > 138;
            var active = outside || (Math.Abs(dx) < 34 && Math.Abs(dy) < 34) ? -1 : Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 3 : 2) : (dy > 0 ? 1 : 0);
            var paths = pad.Children.OfType<Path>().ToList();
            var icons = pad.Children.OfType<PackIconMaterial>().ToList();
            for (var i = 0; i < paths.Count; i++)
            {
                var selected = i == active;
                paths[i].Fill = Brush(selected ? (pressed ? "#2F8FE8" : "#7895A0") : "#5E7883");
                paths[i].Stroke = Brush(selected ? (pressed ? "#D9F1FF" : "#8CCBFF") : "#344A53");
                paths[i].StrokeThickness = selected && pressed ? 2.5 : selected ? 2.0 : 1.5;
                if (i < icons.Count) icons[i].Foreground = selected ? (pressed ? Brushes.White : Brush("#E8F7FF")) : Brushes.White;
            }
        }

        private static Geometry CreateSectorGeometry(double startAngle, double endAngle)
        {
            const double center = 69; const double outer = 68; const double inner = 34;
            var startOuter = PointOnCircle(center, outer, startAngle); var endOuter = PointOnCircle(center, outer, endAngle);
            var endInner = PointOnCircle(center, inner, endAngle); var startInner = PointOnCircle(center, inner, startAngle);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(startOuter, true, true);
                context.ArcTo(endOuter, new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, true);
                context.LineTo(endInner, true, true);
                context.ArcTo(startInner, new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            geometry.Freeze(); return geometry;
        }

        private static Point PointOnCircle(double center, double radius, double angle)
        {
            var radians = angle * Math.PI / 180.0;
            return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        }

        private void Direction(Grid g, string text, int r, int c, double p, double t, double z) { var center = r == 1 && c == 1; var b = Button(string.Empty, "Điều khiển PTZ"); b.Width = 46; b.Height = 46; b.Margin = new Thickness(0); b.Tag = center ? null : Wedge(r, c); b.Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, RenderTransform = new TranslateTransform((c == 0 ? -11 : c == 2 ? 11 : 0), (r == 0 ? -10 : r == 2 ? 10 : 0)) }; b.Style = center ? RoundStyle("#9BACB5", "#344A53", 23) : WedgeStyle(); if (center) b.IsHitTestVisible = false; b.PreviewMouseLeftButtonDown += (s, e) => { if (_pressed) return; b.CaptureMouse(); BeginMove(p, t, z, b); e.Handled = true; }; b.PreviewMouseLeftButtonUp += (s, e) => { StopCurrentMove(); if (b.IsMouseCaptured) b.ReleaseMouseCapture(); e.Handled = true; }; Grid.SetRow(b, r); Grid.SetColumn(b, c); g.Children.Add(b); }
        private static string Wedge(int r, int c) { if (r == 0) return "M23,0 A34,34 0 0 1 46,23 L31,30 A13,13 0 0 0 15,30 L0,23 A34,34 0 0 1 23,0 Z"; if (r == 2) return "M0,23 A34,34 0 0 1 23,46 A34,34 0 0 1 46,23 L31,16 A13,13 0 0 0 15,16 Z"; if (c == 0) return "M0,23 A34,34 0 0 1 23,0 L30,15 A13,13 0 0 0 30,31 L23,46 A34,34 0 0 1 0,23 Z"; return "M23,0 A34,34 0 0 1 46,23 A34,34 0 0 1 23,46 L16,31 A13,13 0 0 0 16,15 Z"; }
        private void BindHold(Button button, double p, double t, double z)
        {
            button.PreviewMouseLeftButtonDown += (s, e) => { button.CaptureMouse(); BeginMove(p, t, z, button); e.Handled = true; };
            button.PreviewMouseLeftButtonUp += (s, e) => { StopCurrentMove(); if (button.IsMouseCaptured) button.ReleaseMouseCapture(); e.Handled = true; };
            button.LostMouseCapture += (s, e) => StopCurrentMove();
        }
        private static void BindVisualState(Button button)
        {
            var icon = button.Content as PackIconMaterial;
            var face = new Border { Background = Brush("#0A2641"), BorderBrush = Brush("#365B7D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 5), Child = icon, IsHitTestVisible = false };
            button.Content = face;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.MouseEnter += (s, e) => { button.Cursor = Cursors.Hand; face.Background = Brush("#164B78"); face.BorderBrush = Brush("#62B8FF"); button.RenderTransform = new ScaleTransform(1.08, 1.08); if (icon != null) icon.Foreground = Brush("#8FD3FF"); };
            button.MouseLeave += (s, e) => { button.Cursor = Cursors.Arrow; face.Background = Brush("#0A2641"); face.BorderBrush = Brush("#365B7D"); button.RenderTransform = new ScaleTransform(1.0, 1.0); if (icon != null) icon.Foreground = Brushes.White; };
            button.PreviewMouseLeftButtonDown += (s, e) => { face.Background = Brush("#226FAE"); face.BorderBrush = Brush("#BDE5FF"); button.RenderTransform = new ScaleTransform(0.94, 0.94); if (icon != null) icon.Foreground = Brush("#D9F1FF"); };
            button.PreviewMouseLeftButtonUp += (s, e) => { var over = button.IsMouseOver; face.Background = Brush(over ? "#164B78" : "#0A2641"); face.BorderBrush = Brush(over ? "#62B8FF" : "#365B7D"); button.RenderTransform = new ScaleTransform(over ? 1.08 : 1.0, over ? 1.08 : 1.0); if (icon != null) icon.Foreground = over ? Brush("#8FD3FF") : Brushes.White; };
        }
        private static Button ZoomButton(PackIconMaterialKind iconKind, string tip)
        {
            var icon = new PackIconMaterial { Kind = iconKind, Width = 20, Height = 20, Foreground = Brushes.White, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var face = new Border { Width = 52, Height = 40, Background = Brush("#0A2641"), BorderBrush = Brush("#365B7D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = icon, IsHitTestVisible = false };
            var button = new Button { Content = face, ToolTip = tip, Width = 52, Height = 40, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
            button.MouseEnter += (s, e) => { face.Background = Brush("#1C5B8F"); face.BorderBrush = Brush("#70C5FF"); icon.Foreground = Brush("#D9F1FF"); face.RenderTransform = new ScaleTransform(1.05, 1.05); face.RenderTransformOrigin = new Point(0.5, 0.5); };
            button.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) { face.Background = Brush("#2F8FE8"); face.BorderBrush = Brush("#E5F6FF"); face.RenderTransform = new ScaleTransform(0.93, 0.93); } else { face.Background = Brush("#1C5B8F"); face.BorderBrush = Brush("#70C5FF"); face.RenderTransform = new ScaleTransform(1.05, 1.05); } };
            button.MouseLeave += (s, e) => { face.Background = Brush("#0A2641"); face.BorderBrush = Brush("#365B7D"); icon.Foreground = Brushes.White; face.RenderTransform = new ScaleTransform(1, 1); };
            button.PreviewMouseLeftButtonDown += (s, e) => { face.Background = Brush("#2F8FE8"); face.BorderBrush = Brush("#E5F6FF"); icon.Foreground = Brushes.White; face.RenderTransform = new ScaleTransform(0.93, 0.93); face.RenderTransformOrigin = new Point(0.5, 0.5); };
            button.PreviewMouseLeftButtonUp += (s, e) => { var over = button.IsMouseOver; face.Background = Brush(over ? "#1C5B8F" : "#0A2641"); face.BorderBrush = Brush(over ? "#70C5FF" : "#365B7D"); icon.Foreground = over ? Brush("#D9F1FF") : Brushes.White; face.RenderTransform = new ScaleTransform(over ? 1.05 : 1, over ? 1.05 : 1); };
            return button;
        }
        private void BeginMove(double p, double t, double z, Button source)
        {
            if (_pressed) return;
            if (_cancel.IsCancellationRequested) return;
            _movementCancel.Cancel(); _movementCancel = new CancellationTokenSource();
            _pressed = true; _active = source; var id = ++_session; LoggerManager.LogInfo($"PTZ begin camera={_cameraId} session={id} pan={p} tilt={t} zoom={z}"); _ = Hold(p, t, z, id, _movementCancel.Token);
        }
        private async Task Hold(double p, double t, double z, int id, CancellationToken movementToken)
        {
            try
            {
                while (id == _session && _pressed && !_cancel.IsCancellationRequested)
                {
                    _ = Move(p, t, z, id, movementToken);
                    await Task.Delay(PtzRenewalIntervalMilliseconds, movementToken).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) { }
        }
        private void StopActive() { StopCurrentMove(); }
        private void StopCurrentMove(bool sendStop = true)
        {
            if (!_pressed && _active == null) return;
            var stoppedSession = _session; _pressed = false; _active = null; ++_session; _requestInFlight = false; _movementCancel.Cancel(); LoggerManager.LogInfo($"PTZ stop camera={_cameraId} session={stoppedSession} nextSession={_session} sendStop={sendStop}"); if (sendStop) _ = StopMove();
        }
        private async Task<bool> Move(double p, double t, double z, int id, CancellationToken movementToken)
        {
            if (id != _session || _cancel.IsCancellationRequested || movementToken.IsCancellationRequested) return false;
            var panSpeed = NormalizeSpeed(_pan.Value); var tiltSpeed = NormalizeSpeed(_tilt.Value); var zoomSpeed = NormalizeSpeed(_zoom.Value);
            var movement = new ApiManager.CameraPtzMovement { duration = PtzMovementSliceSeconds, pan = p * panSpeed, tilt = t * tiltSpeed, zoom = z * zoomSpeed };
            LoggerManager.LogDebug($"PTZ move camera={_cameraId} session={id} pan={movement.pan:0.###} tilt={movement.tilt:0.###} zoom={movement.zoom:0.###} speeds={_pan.Value:0.#}/{_tilt.Value:0.#}/{_zoom.Value:0.#} duration={movement.duration}");
            var result = await ApiManager.Instance.MoveCameraPtzAsync(_cameraId, movement, movementToken).ConfigureAwait(true);
            LoggerManager.LogDebug($"PTZ move result camera={_cameraId} session={id} success={result}");
            return result;
        }
        private Task<bool> Move(double p, double t, double z) => Move(p, t, z, _session, _movementCancel.Token);
        private static double NormalizeSpeed(double speed) => Math.Max(0.0, Math.Min(1.0, speed / 10.0));
        private async Task StopMove()
        {
            try
            {
                LoggerManager.LogDebug($"PTZ stop request camera={_cameraId} endpoint=/api/cameras/{_cameraId}/ptz/stop");
                var result = await ApiManager.Instance.StopCameraPtzAsync(_cameraId, _cancel.Token).ConfigureAwait(true);
                LoggerManager.LogDebug($"PTZ stop result camera={_cameraId} success={result}");
            }
            catch (OperationCanceledException) { }
        }
        private async Task LoadPresets() { try { _presets.Items.Clear(); _presets.Items.Add(EmptyPreset()); _presets.SelectedIndex = 0; foreach (var p in await ApiManager.Instance.GetCameraPtzPresetsAsync(_cameraId, _cancel.Token)) _presets.Items.Add(p); _presets.DisplayMemberPath = "Name"; } catch (OperationCanceledException) { } }
        private static ApiManager.CameraPtzPreset EmptyPreset() { return new ApiManager.CameraPtzPreset { Name = "Chọn vị trí", Token = string.Empty }; }
        private static void Speed(Panel p, string text, Slider s) { p.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) }); p.Children.Add(s); }
        private static Slider SliderOf(double value) { return new Slider { Minimum = 1, Maximum = 10, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 5) }; }
        private static Button Header(string text, string tip) { var b = StyledButton(string.Empty, tip, 34, 30, 16, "#0A2235"); b.Content = text == "×" ? (UIElement)new PackIconMaterial { Kind = PackIconMaterialKind.Close, Width = 15, Height = 15 } : HeaderIcon(PackIconMaterialKind.PowerPlug); return b; }
        private static UIElement HeaderIcon(PackIconMaterialKind kind) { return new PackIconMaterial { Kind = kind, Width = 15, Height = 15 }; }
        private static Button Button(string text, string tip) { return StyledButton(text, tip, 48, 42, 17, "#0A2641"); }
        private static Button StyledButton(string text, string tip, double width, double height, double fontSize, string background) { var normal = Brush(background); var hover = Brush("#164B78"); var pressed = Brush("#226FAE"); var borderNormal = Brush("#365B7D"); var borderHover = Brush("#62B8FF"); var b = new Button { Content = text, ToolTip = tip, Width = width, Height = height, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Foreground = Brushes.White, Background = normal, BorderBrush = borderNormal, BorderThickness = new Thickness(1) }; var style = new Style(typeof(System.Windows.Controls.Button)); style.Setters.Add(new Setter(Control.BackgroundProperty, normal)); style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); style.Setters.Add(new Setter(Control.BorderBrushProperty, borderNormal)); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); style.Triggers.Add(new Trigger { Property = System.Windows.Controls.Button.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, hover), new Setter(Control.BorderBrushProperty, borderHover) } }); style.Triggers.Add(new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, pressed), new Setter(Control.BorderBrushProperty, Brush("#BDE5FF")) } }); var template = new ControlTemplate(typeof(System.Windows.Controls.Button)); var border = new FrameworkElementFactory(typeof(Border)); border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); template.VisualTree = border; style.Setters.Add(new Setter(Control.TemplateProperty, template)); b.Style = style; return b; }
        private static Style RoundStyle(string background, string border, double radius) { var style = new Style(typeof(System.Windows.Controls.Button)); style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(background))); style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(border))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); var template = new ControlTemplate(typeof(System.Windows.Controls.Button)); var root = new FrameworkElementFactory(typeof(Border)); root.SetValue(Border.BackgroundProperty, Brush(background)); root.SetValue(Border.BorderBrushProperty, Brush(border)); root.SetValue(Border.BorderThicknessProperty, new Thickness(1)); root.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius)); var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); root.AppendChild(content); template.VisualTree = root; style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style; }
        private static Style WedgeStyle() { var style = new Style(typeof(System.Windows.Controls.Button)); var normal = Brush("#5E7883"); var hover = Brush("#718D98"); var pressed = Brush("#89A4AE"); var border = Brush("#344A53"); style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); style.Setters.Add(new Setter(Control.BackgroundProperty, normal)); style.Setters.Add(new Setter(Control.BorderBrushProperty, border)); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); style.Triggers.Add(new Trigger { Property = System.Windows.Controls.Button.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, hover) } }); style.Triggers.Add(new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, pressed), new Setter(Control.BorderBrushProperty, Brush("#AFC8D1")) } }); var template = new ControlTemplate(typeof(System.Windows.Controls.Button)); var grid = new FrameworkElementFactory(typeof(Grid)); var path = new FrameworkElementFactory(typeof(Path)); path.SetBinding(Path.DataProperty, new System.Windows.Data.Binding("Tag") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); path.SetBinding(Path.FillProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); path.SetBinding(Path.StrokeProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); path.SetValue(Path.StrokeThicknessProperty, 1.5); grid.AppendChild(path); var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); grid.AppendChild(content); template.VisualTree = grid; style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style; }
        private static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private static void Configure(ComboBox b) { var background = Brush("#0A2235"); var border = Brush("#245B9B"); var hover = Brush("#123B66"); b.Foreground = Brushes.White; b.Background = background; b.BorderBrush = border; b.Padding = new Thickness(7, 0, 3, 0); b.VerticalContentAlignment = VerticalAlignment.Center; b.Resources[SystemColors.WindowBrushKey] = background; b.Resources[SystemColors.ControlBrushKey] = background; b.Resources[SystemColors.HighlightBrushKey] = hover; b.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White; b.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = hover; var textStyle = new Style(typeof(TextBox)); textStyle.Setters.Add(new Setter(Control.BackgroundProperty, background)); textStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); textStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); textStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 0, 0))); b.Resources[typeof(TextBox)] = textStyle; var itemStyle = new Style(typeof(ComboBoxItem)); itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, background)); itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5))); itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left)); itemStyle.Triggers.Add(new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, hover), new Setter(Control.ForegroundProperty, Brushes.White) } }); b.ItemContainerStyle = itemStyle; b.Style = new Style(typeof(ComboBox)) { Setters = { new Setter(Control.BackgroundProperty, background), new Setter(Control.ForegroundProperty, Brushes.White), new Setter(Control.BorderBrushProperty, border), new Setter(Control.BorderThicknessProperty, new Thickness(1)), new Setter(Control.PaddingProperty, new Thickness(7, 0, 3, 0)), new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center) } }; b.Loaded += (s, e) => PaintComboTemplate(b, background, border); b.DropDownOpened += (s, e) => { var popup = b.Template.FindName("PART_Popup", b) as Popup; if (popup != null && popup.Child != null) PaintComboTemplate(popup.Child, background, border); }; }
        private static void PaintComboTemplate(DependencyObject root, Brush background, Brush border) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); var control = child as Control; if (control != null) { control.Background = background; control.Foreground = Brushes.White; control.BorderBrush = border; } var childBorder = child as Border; if (childBorder != null) { childBorder.Background = background; childBorder.BorderBrush = border; } PaintComboTemplate(child, background, border); } }
    }
}
