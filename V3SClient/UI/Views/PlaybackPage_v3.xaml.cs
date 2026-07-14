using System.Windows;
using System.Windows.Controls;
using System;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class PlaybackPage_v3 : UserControl
    {
        public PlaybackPage_v3()
        {
            InitializeComponent();
            DataContext = new PlaybackViewModel_v3();
            Loaded += OnLoaded;
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (PlaybackHost.Content != null)
                return;

            try
            {
                PlaybackHost.Navigate(new VPlayback(GlobalSystem.Instance.CameraGroups.CamGroupList));
            }
            catch (Exception ex)
            {
                // The legacy playback control is isolated from the _v3 shell.  A missing
                // optional playback dependency must not terminate Live View or the shell.
                LoggerManager.LogException(ex, "Playback _v3 initialization failed");
                PlaybackHost.Content = new TextBlock
                {
                    Text = "Playback is unavailable. The detailed error has been written to the application log.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
    }
}
