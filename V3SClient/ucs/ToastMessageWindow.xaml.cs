using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for ToastMessageWindow.xaml
    /// </summary>
    public partial class ToastMessageWindow : Window
    {
        public new string Title { get; set; }
        public string Message { get; set; }
        public ImageSource IconSource { get; set; }
        public Brush BackgroundBrush { get; set; }

        private DispatcherTimer timer;
        private int duration = 3000; // ms
        private int fadeOutDuration = 1000;

        public ToastMessageWindow(string title, string msg, ToastType type)
        {
            InitializeComponent();
            DataContext = this;
            Title = title;
            Message = msg;
            BackgroundBrush = GetBrush(type);
            IconSource = GetIcon(type);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Fade in
            this.Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(duration - fadeOutDuration);
            timer.Tick += StartFadeOut;
            timer.Start();
        }
        private void StartFadeOut(object sender, EventArgs e)
        {
            timer.Stop();

            var fadeOut = new DoubleAnimation
            {
                From = this.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(fadeOutDuration),
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (s, ev) => this.Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
        private void PositionWindow()
        {
            this.Left = SystemParameters.WorkArea.Width - this.Width - 10;
            this.Top = SystemParameters.WorkArea.Height - this.Height - 10;
        }

        private Brush GetBrush(ToastType type)
        {
            switch (type)
            {
                case ToastType.Success:
                    return Brushes.SeaGreen;
                case ToastType.Warning:
                    return Brushes.DarkOrange;
                case ToastType.Error:
                    return Brushes.DarkRed;
                case ToastType.Info:
                default:
                    return Brushes.RoyalBlue;
            }
        }

        private ImageSource GetIcon(ToastType type)
        {
            string path = "/V3SClient;component/images/toast/";
            switch (type)
            {
                case ToastType.Success:
                    path += "toast_success.png"; break;
                case ToastType.Warning:
                    path += "toast_warning.png"; break;
                case ToastType.Error:
                    path += "toast_error.png"; break;
                case ToastType.Info:
                default:
                    path += "toast_info.png"; break;
            }
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Relative);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Toast icon unavailable: " + ex.Message);
                return null;
            }
        }
    }

    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }
}



















