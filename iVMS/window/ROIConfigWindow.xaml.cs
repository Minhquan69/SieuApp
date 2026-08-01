using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using V3SClient.libs;
using static V3SClient.libs.ApiManager;
using System.Text;

namespace V3SClient.window
{
    public partial class ROIConfigWindow : Window
    {
        private string _camId;
        private ApiManager.RoiInfo _roi;
        private ObservableCollection<PointViewModel> _points = new ObservableCollection<PointViewModel>();
        private bool _isDrawing = true;

        public ROIConfigWindow(string camId, ApiManager.RoiInfo roi = null)
        {
            InitializeComponent();
            _camId = camId;
            _roi = roi;
            LbPoints.ItemsSource = _points;
            Loaded += ROIConfigWindow_Loaded;
        }

        private async void ROIConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSnapshot();
            await LoadAIServices();

            if (_roi != null)
            {
                TxtName.Text = _roi.Name;
                RbPolygon.IsChecked = _roi.RoiType == "polygon";
                RbLine.IsChecked = _roi.RoiType == "line";
                ChkActive.IsChecked = _roi.IsActive;
                CboAiService.SelectedValue = _roi.AiServiceId;

                if (_roi.Rule != null)
                {
                    try
                    {
                        var rule = JToken.FromObject(_roi.Rule);
                        TxtRule.Text = JsonConvert.SerializeObject(rule, Formatting.Indented);

                        // Load Template
                        string templateName = rule["name"]?.ToString();
                        foreach (ComboBoxItem item in CboTemplate.Items)
                        {
                            if (item.Content?.ToString() == templateName)
                            {
                                CboTemplate.SelectedItem = item;
                                ApplyTemplate(templateName);
                                break;
                            }
                        }

                        // Load Extended Trigger
                        var trigger = rule["trigger"];
                        if (trigger != null)
                        {
                            SetComboByTag(CboEventType, trigger["event_type"]?.ToString());
                            SetComboByTag(CboObjectType, trigger["object_type"]?.ToString());
                            SetComboByTag(CboDirection, trigger["direction"]?.ToString() ?? "ANY");
                        }

                        // Load Params
                        var rparams = rule["params"];
                        if (rparams != null)
                        {
                            if (rparams["confidence_min"] != null)
                                SldConfidence.Value = (double)rparams["confidence_min"];
                            
                            ChkBlacklist.IsChecked = (bool?)rparams["is_blacklist"] ?? false;
                        }

                        // Load Actions
                        var actions = rule["actions"];
                        if (actions != null)
                        {
                            var include = actions["include"] as JArray;
                            ChkCrop.IsChecked = include?.Any(x => x.ToString() == "object_crop") ?? false;
                            ChkWarning.IsChecked = (bool?)actions["send_warning"] ?? false;
                        }

                        // Load Extended ROI Properties
                        var props = rule["roi_properties"];
                        if (props != null)
                        {
                            // Direction (redundant but consistent)
                            string dir = props["direction"]?.ToString() ?? "ANY";
                            SetComboByTag(CboDirection, dir);

                            // Active Days
                            var days = props["active_days"]?.ToList();
                            if (days != null)
                            {
                                ChkD2.IsChecked = days.Any(d => d.ToString() == "1");
                                ChkD3.IsChecked = days.Any(d => d.ToString() == "2");
                                ChkD4.IsChecked = days.Any(d => d.ToString() == "3");
                                ChkD5.IsChecked = days.Any(d => d.ToString() == "4");
                                ChkD6.IsChecked = days.Any(d => d.ToString() == "5");
                                ChkD7.IsChecked = days.Any(d => d.ToString() == "6");
                                ChkD8.IsChecked = days.Any(d => d.ToString() == "7");
                            }

                            // Active Windows
                            var windows = props["active_windows"] as JArray;
                            if (windows != null && windows.Count > 0)
                            {
                                var firstWin = windows[0] as JArray;
                                if (firstWin != null && firstWin.Count >= 2)
                                {
                                    TxtTimeStart.Text = firstWin[0]?.ToString() ?? "00:00";
                                    TxtTimeEnd.Text = firstWin[1]?.ToString() ?? "23:59";
                                }
                                else if (windows.Count >= 2 && windows[0].Type == JTokenType.String)
                                {
                                    // Handle flattened case ["00:00", "23:59"]
                                    TxtTimeStart.Text = windows[0]?.ToString() ?? "00:00";
                                    TxtTimeEnd.Text = windows[1]?.ToString() ?? "23:59";
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error parsing existing rule: " + ex.Message);
                        TxtRule.Text = JsonConvert.SerializeObject(_roi.Rule, Formatting.Indented);
                    }
                }

                if (_roi.Points != null)
                {
                    foreach (var p in _roi.Points)
                    {
                        _points.Add(new PointViewModel { X = p.X, Y = p.Y });
                    }
                    Redraw();
                }
            }
        }

        private async System.Threading.Tasks.Task LoadAIServices()
        {
            try
            {
                var services = await ApiManager.Instance.GetAIServicesAsync(CancellationToken.None);
                CboAiService.ItemsSource = services;
                if (_roi != null) CboAiService.SelectedValue = _roi.AiServiceId;
                else if (services.Count > 0) CboAiService.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading AI services: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task LoadSnapshot()
        {
            try
            {
                string imageUrl = await ApiManager.Instance.GetCameraSnapshotAsync(_camId, CancellationToken.None);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var bitmap = new BitmapImage(new Uri(imageUrl));
                    ImgSnapshot.Source = bitmap;
                    TxtResolutionInfo.Text = $"Độ phân giải: {bitmap.PixelWidth}x{bitmap.PixelHeight} (Tự động)";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể tải ảnh snapshot: " + ex.Message);
            }
        }


        private void RoiCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(RoiCanvas);
                
                // Convert to normalized coordinates
                double normX = pos.X / RoiCanvas.ActualWidth;
                double normY = pos.Y / RoiCanvas.ActualHeight;

                if (RbLine.IsChecked == true && _points.Count >= 2)
                {
                    MessageBox.Show("Loại đường kẻ chỉ cho phép tối đa 2 điểm.");
                    return;
                }

                _points.Add(new PointViewModel { X = (float)normX, Y = (float)normY });
                Redraw();
            }
        }

        private void RoiCanvas_MouseMove(object sender, MouseEventArgs e)
        {
        }

        private void Redraw()
        {
            RoiCanvas.Children.Clear();
            if (_points.Count == 0) return;

            // Draw regions/polygons can be enhanced here but let's keep it simple as requested
            for (int i = 0; i < _points.Count - 1; i++)
            {
                DrawLine(_points[i], _points[i+1]);
            }

            if (RbPolygon.IsChecked == true && _points.Count > 2)
            {
                DrawLine(_points.Last(), _points.First(), true);
            }

            foreach (var p in _points)
            {
                DrawPoint(p);
            }
        }

        private void DrawPoint(PointViewModel p)
        {
            double x = p.X * RoiCanvas.ActualWidth;
            double y = p.Y * RoiCanvas.ActualHeight;

            Ellipse ellipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Red,
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            Canvas.SetLeft(ellipse, x - 4);
            Canvas.SetTop(ellipse, y - 4);
            RoiCanvas.Children.Add(ellipse);
        }

        private void DrawLine(PointViewModel p1, PointViewModel p2, bool isClosing = false)
        {
            Line line = new Line
            {
                X1 = p1.X * RoiCanvas.ActualWidth,
                Y1 = p1.Y * RoiCanvas.ActualHeight,
                X2 = p2.X * RoiCanvas.ActualWidth,
                Y2 = p2.Y * RoiCanvas.ActualHeight,
                Stroke = isClosing ? Brushes.Yellow : Brushes.Cyan,
                StrokeThickness = 2,
                StrokeDashArray = isClosing ? new DoubleCollection { 2, 2 } : null
            };
            RoiCanvas.Children.Add(line);
        }


        private void SetComboByTag(ComboBox combo, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _points.Clear();
            Redraw();
        }

        private void CboTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboTemplate.SelectedItem is ComboBoxItem item)
            {
                string templateName = item.Tag?.ToString();
                if (templateName != "custom")
                {
                    ApplyTemplate(templateName);
                }
            }
        }

        private void ApplyTemplate(string name)
        {
            switch (name)
            {
                case "Face Detected":
                    SetComboByTag(CboEventType, "object_appear");
                    SetComboByTag(CboObjectType, "face");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = true;
                    ChkWarning.IsChecked = false;
                    break;
                case "Suspect Face Detected":
                    SetComboByTag(CboEventType, "object_appear");
                    SetComboByTag(CboObjectType, "face");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = true;
                    ChkCrop.IsChecked = true;
                    ChkWarning.IsChecked = true;
                    break;
                case "License Plate Detected":
                    SetComboByTag(CboEventType, "object_appear");
                    SetComboByTag(CboObjectType, "plate");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = true;
                    ChkWarning.IsChecked = false;
                    break;
                case "ROI Overcrowding Detected":
                    SetComboByTag(CboEventType, "roi_overcrowd");
                    SetComboByTag(CboObjectType, "person");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = false;
                    ChkWarning.IsChecked = true;
                    break;
                case "Restricted Zone Intrusion":
                    SetComboByTag(CboEventType, "object_appear");
                    SetComboByTag(CboObjectType, "person");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = true;
                    ChkWarning.IsChecked = true;
                    break;
                case "Line Crossing Detected AB":
                    SetComboByTag(CboEventType, "line_cross");
                    SetComboByTag(CboDirection, "AB");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = false;
                    ChkWarning.IsChecked = true;
                    break;
                case "Line Crossing Detected Any":
                    SetComboByTag(CboEventType, "line_cross");
                    SetComboByTag(CboDirection, "ANY");
                    SldConfidence.Value = 0.5;
                    ChkBlacklist.IsChecked = false;
                    ChkCrop.IsChecked = false;
                    ChkWarning.IsChecked = true;
                    break;
            }
        }

        private void RbType_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                _points.Clear();
                Redraw();
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên vùng ROI.");
                return;
            }

            if (_points.Count < (RbPolygon.IsChecked == true ? 3 : 2))
            {
                MessageBox.Show(RbPolygon.IsChecked == true ? "Vùng đa giác cần ít nhất 3 điểm." : "Đường kẻ cần ít nhất 2 điểm.");
                return;
            }

            JObject ruleObj = null;
            if (!string.IsNullOrWhiteSpace(TxtRule.Text))
            {
                try
                {
                    ruleObj = JObject.Parse(TxtRule.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Tham số Rule không hợp lệ (định dạng JSON sai): " + ex.Message);
                    return;
                }
            }
            else {
                ruleObj = new JObject();
            }

            // Append ROI properties to ruleObj
            var activeDays = new List<int>();
            if (ChkD2.IsChecked == true) activeDays.Add(1);
            if (ChkD3.IsChecked == true) activeDays.Add(2);
            if (ChkD4.IsChecked == true) activeDays.Add(3);
            if (ChkD5.IsChecked == true) activeDays.Add(4);
            if (ChkD6.IsChecked == true) activeDays.Add(5);
            if (ChkD7.IsChecked == true) activeDays.Add(6);
            if (ChkD8.IsChecked == true) activeDays.Add(7);

            // Construct Rule Object from UI fields
            var trigger = new JObject();
            trigger["event_type"] = (CboEventType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            trigger["object_type"] = (CboObjectType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            trigger["direction"] = (CboDirection.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ANY";

            var rparams = new JObject();
            rparams["confidence_min"] = SldConfidence.Value;
            rparams["is_blacklist"] = ChkBlacklist.IsChecked == true;

            var actions = new JObject();
            var includeArray = new JArray();
            if (ChkCrop.IsChecked == true) includeArray.Add("object_crop");
            actions["include"] = includeArray;
            actions["send_warning"] = ChkWarning.IsChecked == true;

            ruleObj["name"] = (CboTemplate.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Custom Rule";
            ruleObj["trigger"] = trigger;
            ruleObj["params"] = rparams;
            ruleObj["actions"] = actions;

            var roiProps = new JObject();
            roiProps["direction"] = trigger["direction"];
            roiProps["active_days"] = JArray.FromObject(activeDays);
            
            // Nested array [[start, end]] to match Web expectation
            var window = new JArray { TxtTimeStart.Text, TxtTimeEnd.Text };
            roiProps["active_windows"] = new JArray { window };

            ruleObj["roi_properties"] = roiProps;

            string resolution = _roi?.Resolution ?? "1920x1080";
            if (ImgSnapshot.Source is BitmapSource bitmap)
            {
                resolution = $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
            }

            var request = new
            {
                name = TxtName.Text,
                roi_type = RbPolygon.IsChecked == true ? "polygon" : "line",
                points = _points.Select(p => new { x = p.X, y = p.Y }).ToList(),
                is_active = ChkActive.IsChecked ?? true,
                camera_id = _camId,
                ai_service_id = CboAiService.SelectedValue,
                rule = ruleObj,
                resolution = resolution
            };

            bool success;
            if (_roi == null)
            {
                success = await ApiManager.Instance.CreateRoiAsync(request, CancellationToken.None);
            }
            else
            {
                success = await ApiManager.Instance.UpdateRoiAsync(_roi.Id, request, CancellationToken.None);
            }

            if (success)
            {
                MessageBox.Show("Lưu ROI thành công.");
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Có lỗi khi lưu ROI.");
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        public class PointViewModel
        {
            public float X { get; set; }
            public float Y { get; set; }
            public string DisplayText => $"({X:F2}, {Y:F2})";
        }
    }
}

















