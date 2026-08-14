using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    public sealed class PtzControlWindow_v3 : Window
    {
        private readonly string _cameraId;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Slider _panSpeed = MakeSlider(6), _tiltSpeed = MakeSlider(5), _zoomSpeed = MakeSlider(4);
        private readonly ComboBox _presets = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 8, 8, 0) };
        private int _moveSession;
        public PtzControlWindow_v3(string cameraId, Window owner)
        {
            _cameraId = cameraId; Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 330; Height = 560; ResizeMode = ResizeMode.NoResize; Title = "PTZ · " + cameraId;
            Background = new SolidColorBrush(Color.FromRgb(7, 23, 36)); Foreground = Brushes.White; Content = BuildContent();
            Loaded += async (s, e) => await LoadPresetsAsync(); Closed += (s, e) => { _lifetime.Cancel(); StopMove(); };
        }
        private static Slider MakeSlider(double value) => new Slider { Minimum = 1, Maximum = 10, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        private UIElement BuildContent()
        {
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "PTZ  •  " + _cameraId, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)) });
            root.Children.Add(new TextBlock { Text = "Điều khiển camera", Margin = new Thickness(0, 4, 0, 18), Foreground = Brushes.LightGray });
            var pad = new Grid { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
            for (var i = 0; i < 3; i++) { pad.RowDefinitions.Add(new RowDefinition()); pad.ColumnDefinitions.Add(new ColumnDefinition()); }
            AddButton(pad, "▲", 0, 1, 0, 1, 0); AddButton(pad, "◀", 1, 0, -1, 0, 0); AddButton(pad, "●", 1, 1, 0, 0, 0); AddButton(pad, "▶", 1, 2, 1, 0, 0); AddButton(pad, "▼", 2, 1, 0, -1, 0);
            root.Children.Add(pad); AddSpeed(root, "Tốc độ Pan", _panSpeed); AddSpeed(root, "Tốc độ Tilt", _tiltSpeed); AddSpeed(root, "Tốc độ Zoom", _zoomSpeed);
            root.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 8) }); root.Children.Add(new TextBlock { Text = "Vị trí đặt trước", FontWeight = FontWeights.SemiBold });
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(_presets);
            var go = new Button { Content = "Đi tới", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 6, 12, 6) }; go.Click += async (s, e) => { var p = _presets.SelectedItem as ApiManager.CameraPtzPreset; if (p != null) await ApiManager.Instance.GotoCameraPtzPresetAsync(_cameraId, p.Token); }; row.Children.Add(go); root.Children.Add(row);
            return root;
        }
        private void AddSpeed(Panel root, string label, Slider slider) { root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0) }); root.Children.Add(slider); }
        private void AddButton(Grid grid, string text, int r, int c, double pan, double tilt, double zoom)
        {
            var b = new Button { Content = text, FontSize = 22, Margin = new Thickness(3), Background = new SolidColorBrush(Color.FromRgb(15, 45, 75)), Foreground = Brushes.White };
            b.PreviewMouseLeftButtonDown += async (s, e) => { e.Handled = true; var session = ++_moveSession; try { while (session == _moveSession && IsVisible && !_lifetime.IsCancellationRequested) { await ApiManager.Instance.MoveCameraPtzAsync(_cameraId, new ApiManager.CameraPtzMovement { duration = 10, pan = pan, tilt = tilt, zoom = zoom, pan_speed = _panSpeed.Value, tilt_speed = _tiltSpeed.Value, zoom_speed = _zoomSpeed.Value }, _lifetime.Token); await Task.Delay(120, _lifetime.Token); } } catch (OperationCanceledException) { } };
            b.PreviewMouseLeftButtonUp += (s, e) => { ++_moveSession; StopMove(); }; b.MouseLeave += (s, e) => { if (e.LeftButton != MouseButtonState.Pressed) { ++_moveSession; StopMove(); } }; Grid.SetRow(b, r); Grid.SetColumn(b, c); grid.Children.Add(b);
        }
        private async void StopMove() { try { await ApiManager.Instance.MoveCameraPtzAsync(_cameraId, new ApiManager.CameraPtzMovement { duration = 1 }, _lifetime.Token); } catch { } }
        private async Task LoadPresetsAsync() { foreach (var p in await ApiManager.Instance.GetCameraPtzPresetsAsync(_cameraId, _lifetime.Token)) _presets.Items.Add(p); _presets.DisplayMemberPath = "Name"; }
    }
}
