using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using V3SClient.UI.Views;
using V3SClient.window;

namespace V3SClient.ucs
{
    public partial class HeaderControl_v3 : UserControl
    {
        public event EventHandler SwitchClientRequested;
        public event EventHandler LogoutRequested;

        public HeaderControl_v3()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Keep controls in the header clickable; only empty header space
            // behaves as the native title bar, exactly like the base client.
            for (DependencyObject source = e.OriginalSource as DependencyObject;
                 source != null;
                 source = VisualTreeHelper.GetParent(source))
            {
                if (source is Button || source is ComboBox || source is TextBox)
                    return;
            }

            var shell = Window.GetWindow(this) as ShellWindow_v3;
            if (shell == null) return;

            if (e.ClickCount == 2)
            {
                shell.ToggleVirtualDesktopMode();
                UpdateWindowModeIcon(shell);
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
                shell.BeginMoveFromHeader();
        }

        private void AccountButton_Click(object s, RoutedEventArgs e) { AccountPopup.IsOpen = true; }
        private void DisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            DisplaySettingsPanel.Visibility = DisplaySettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateCameraDisplaySettingVisuals();
        }

        private void PreserveScale_Click(object sender, RoutedEventArgs e)
        {
            WhepPlayer_v3.PreserveCameraAspectRatio = true;
            UpdateCameraDisplaySettingVisuals();
        }

        private void FillScale_Click(object sender, RoutedEventArgs e)
        {
            WhepPlayer_v3.PreserveCameraAspectRatio = false;
            UpdateCameraDisplaySettingVisuals();
        }

        private void UpdateCameraDisplaySettingVisuals()
        {
            var selected = (Brush)FindResource("VmsPrimarySofterBrush_v3");
            var normal = Brushes.Transparent;
            var preserve = WhepPlayer_v3.PreserveCameraAspectRatio;
            PreserveScaleButton.Background = preserve ? selected : normal;
            PreserveScaleButton.Foreground = preserve ? (Brush)FindResource("VmsFocusBrush_v3") : (Brush)FindResource("VmsTextSecondaryBrush_v3");
            FillScaleButton.Background = preserve ? normal : selected;
            FillScaleButton.Foreground = preserve ? (Brush)FindResource("VmsTextSecondaryBrush_v3") : (Brush)FindResource("VmsFocusBrush_v3");
        }
        private void MinimizeWindowButton_Click(object s, RoutedEventArgs e)
        {
            var shell = Window.GetWindow(this);
            if (shell != null) shell.WindowState = WindowState.Minimized;
        }
        private void WindowModeButton_Click(object s, RoutedEventArgs e)
        {
            var shell = Window.GetWindow(this) as ShellWindow_v3;
            if (shell != null)
            {
                shell.ToggleVirtualDesktopMode();
                UpdateWindowModeIcon(shell);
            }
        }

        private void UpdateWindowModeIcon(ShellWindow_v3 shell)
        {
            WindowModeIcon.Kind = shell.IsVirtualDesktopMode
                ? PackIconMaterialKind.WindowRestore
                : PackIconMaterialKind.WindowMaximize;
        }

        private void CloseApplicationButton_Click(object s, RoutedEventArgs e)
        {
            var shell = Window.GetWindow(this);
            if (shell != null) shell.Close();
        }
        private void SwitchClient_Click(object s, RoutedEventArgs e) { SwitchClientRequested?.Invoke(this, EventArgs.Empty); }
        private void Logout_Click(object s, RoutedEventArgs e) { LogoutRequested?.Invoke(this, EventArgs.Empty); }
    }
}
