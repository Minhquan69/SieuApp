using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Gst;
using LiveCharts.Wpf;
using OpenCvSharp.Flann;
using SharpDX.Direct3D11;
using V3SClient.libs;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for miniSystemMonitor.xaml
    /// </summary>
    public partial class SystemMonitor : UserControl, IDisposable
    {
        libs.Counter counter = new libs.Counter();
        private Dictionary<string, ProgressBar> progressBars = new Dictionary<string, ProgressBar>();
        private Dictionary<string, Label> labels = new Dictionary<string, Label>();
        private double _currentNetSentMax = 10;
        private double _currentNetReceivedMax = 10;
        DispatcherTimer updateTimer;

        public SystemMonitor()
        {
            InitializeComponent();
            Unloaded += SystemMonitor_Unloaded;

            networkSentChart.SetAxisYLimits(0, _currentNetSentMax);
            networkReceivedChart.SetAxisYLimits(0, _currentNetReceivedMax);

            //Disk
            //Disk Usage
           
            totalDiskFreeLabel.Content = counter.GetFreeSpaceLabel();
            totalDiskUsedLabel.Content = counter.GetUsedSpaceLabel();

            int diskCount=counter.DiskSpaceTotal.Count;

            Grid dynamicGrid = new Grid
            {
                Margin = new Thickness(1,10,1,1),
            };
            // Tạo cột
            for (int i = 0; i < diskCount; i++)
                dynamicGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            
            // Tạo hàng
            for (int j = 0; j < 4; j++)
                dynamicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int index = 0;
            for (int row = 0; row < diskCount; row++)
            {
                if (index >= 0 && index < counter.DiskSpaceTotal.Count)
                {
                    var element = counter.DiskSpaceTotal.ElementAt(index);
                    TextBlock title = new TextBlock
                    {
                        Text = element.Key,
                        Style = (Style)FindResource("titleText"),
                        Margin = new Thickness(5, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Grid.SetRow(title, row);
                    Grid.SetColumn(title, 0);
                    dynamicGrid.Children.Add(title);
                    double diskPercent = counter.GetFreeSpaceGaugePercent(element.Key);
                    // Táº¡o ProgressBar
                    ProgressBar progressBar = new ProgressBar
                    {
                        Name = $"progressBar_{element.Key}",
                        Style = (Style)FindResource("ProgressBarStyle"),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = 60,
                        Height = 12,
                        Minimum = 0,
                        Maximum = 100,
                        Value = diskPercent

                    };
                    progressBars[$"progressBar_{element.Key}"] = progressBar;

                    Grid.SetRow(progressBar, row);
                    Grid.SetColumn(progressBar, 1);
                    Grid.SetColumnSpan(progressBar, 2);
                    dynamicGrid.Children.Add(progressBar);

                    // Tạo Grid con chứa Label và %
                    Grid percentageGrid = new Grid();
                    percentageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                    percentageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    Label label = new Label
                    {
                        Name = $"label_{element.Key}",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 8,
                        FontWeight = FontWeights.SemiBold,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(0),
                        ContentStringFormat = "{0:F1}",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Content= diskPercent
                    };
                    labels[$"label_{element.Key}"] = label;
                    Grid.SetColumn(label, 0);
                    percentageGrid.Children.Add(label);

                    TextBlock percentText = new TextBlock
                    {
                        Text = "%",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 6,
                        Margin = new Thickness(1, 8, 1, 8),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(percentText, 1);
                    percentageGrid.Children.Add(percentText);

                    Grid.SetRow(percentageGrid, row);
                    Grid.SetColumn(percentageGrid, 3);
                    dynamicGrid.Children.Add(percentageGrid);
                    index++;
                }          
            }
            GaugeContainer.Content=dynamicGrid;

            //Initialize timer
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(3);
            updateTimer.Tick += timer_Tick;
            updateTimer.Start();
        }
        void timer_Tick(object sender, EventArgs e)
        {
            if (counter == null) return;
            //CPU Usage
            double currentPerform = counter.PerformanceCPU.NextValue();
            cpuLabel.Content = cpuProgressBar.Value = currentPerform;
            cpuChart.SetVal(currentPerform);

            //RAM Usage
            double ramUse= counter.GetFreeRAMInPercent();
            ramLabel.Content = ramProgressBar.Value = ramUse;

            //Disk
            foreach (var key in progressBars.Keys)
            {
                string diskName = key.Replace("progressBar_", "");
                double newValue = counter.GetFreeSpaceGaugePercent(diskName);
               
                if (progressBars.TryGetValue(key, out ProgressBar progressBar))
                    progressBar.Value = newValue;

                if (labels.TryGetValue($"label_{diskName}", out Label label))
                    label.Content = $"{newValue:F1}";
            }

            //Network Usage
            double netSentMbps= counter.GetNetworkSentBytes();
            networkSentBytesLabel.Content = netSentMbps;
            
            // Dynamic scale for Sent
            double nextSentMax = GetNextRange(netSentMbps);
            if (nextSentMax != _currentNetSentMax)
            {
                _currentNetSentMax = nextSentMax;
                networkSentChart.SetAxisYLimits(0, _currentNetSentMax);
            }
            networkSentChart.SetVal(netSentMbps);

            double netReceivedMbps = counter.GetNetworkReceivedBytes();
            networkReceivedBytesLabel.Content = netReceivedMbps;
            
            // Dynamic scale for Received
            double nextReceivedMax = GetNextRange(netReceivedMbps);
            if (nextReceivedMax != _currentNetReceivedMax)
            {
                _currentNetReceivedMax = nextReceivedMax;
                networkReceivedChart.SetAxisYLimits(0, _currentNetReceivedMax);
            }
            networkReceivedChart.SetVal(netReceivedMbps);
            ////GPU
            var gpuUtil = counter.GetGpuData();
            gpuLabel.Content = gpuProgressBar.Value = gpuUtil.gpu;
            vramLabel.Content = vramProgressBar.Value = gpuUtil.memory;
            gpuChart.SetVal(gpuUtil.gpu);
 
        }

        private void SetUpdateSpeed(string speed)
        {
            switch (speed)
            {
                case "High":
                    updateTimer.Interval = TimeSpan.FromSeconds(1);
                    updateTimer.Start();
                    break;
                case "Normal":
                    updateTimer.Interval = TimeSpan.FromSeconds(5);
                    updateTimer.Start();
                    break;
                case "Low":
                    updateTimer.Interval = TimeSpan.FromSeconds(10);
                    updateTimer.Start();
                    break;
                case "Pause":
                    updateTimer.Stop();
                    break;
            }
        }
        private void Grid_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ContextMenu menu = new ContextMenu();

            MenuItem refreshItem = new MenuItem { Header = "Làm mới ngay" };
            refreshItem.Click += (s, args) => timer_Tick(s,args);
            menu.Items.Add(refreshItem);

            MenuItem updateSpeedItem = new MenuItem { Header = "Tốc độ cập nhật" };

            // Speeds translation mapping
            var speeds = new Dictionary<string, string> { 
                { "High", "Nhanh" }, 
                { "Normal", "Bình thường" }, 
                { "Low", "Chậm" }, 
                { "Pause", "Tạm dừng" } 
            };
            foreach (var speed in speeds)
            {
                MenuItem speedItem = new MenuItem { Header = speed.Value };
                speedItem.Click += (s, args) => SetUpdateSpeed(speed.Key);
                updateSpeedItem.Items.Add(speedItem);
            }

            menu.Items.Add(updateSpeedItem);
            menu.IsOpen = true;
        }
        private double GetNextRange(double value)
        {
            if (value <= 8) return 10;
            if (value <= 18) return 20;
            if (value <= 45) return 50;
            if (value <= 90) return 100;
            if (value <= 180) return 200;
            if (value <= 450) return 500;
            return 1000;
        }
        private void SystemMonitor_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Dispose();
        }

        public void Dispose()
        {
            try
            {
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    updateTimer.Tick -= timer_Tick;
                    updateTimer = null;
                }

                if (counter != null)
                {
                    counter.Dispose();
                    counter = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing SystemMonitor: {ex.Message}");
            }
        }
    }
}

















