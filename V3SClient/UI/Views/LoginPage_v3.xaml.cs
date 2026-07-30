using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class LoginPage_v3 : Page
    {
        private TextBox _visiblePassword;
        private Button _passwordToggle;
        private bool _isPasswordVisible;
        public LoginPage_v3()
        {
            InitializeComponent();
            Loaded += LoginPage_v3_Loaded;
            SizeChanged += (s, e) => { ScheduleProfileLayout(); UpdateResponsiveLoginLayout(); };
            DataContextChanged += LoginPage_v3_DataContextChanged;
            AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPageMouseUp), true);
        }
        private void LoginPage_v3_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleProfileLayout();
            UpdateResponsiveLoginLayout();
            UpdatePlatformCaption(this);
            UpdateApplicationMarketingText(this);
            var cachedLogin = DataContext as LoginViewModel_v3;
            if (cachedLogin != null && string.IsNullOrEmpty(PasswordInput.Password) && !string.IsNullOrEmpty(cachedLogin.Password))
                PasswordInput.Password = cachedLogin.Password;
            UpdateLoginButtonState();
            var parent = VisualTreeHelper.GetParent(PasswordInput) as Panel;
            if (parent == null || _visiblePassword != null) return;
            var index = parent.Children.IndexOf(PasswordInput);
            var host = new Grid { Height = 48, Margin = PasswordInput.Margin };
            PasswordInput.Margin = new Thickness(0); PasswordInput.Padding = new Thickness(48, 10, 42, 10);
            _visiblePassword = new TextBox { Visibility = Visibility.Collapsed, Height = 48, Padding = new Thickness(48, 10, 42, 10), FontSize = 14, Background = PasswordInput.Background, BorderBrush = PasswordInput.BorderBrush, BorderThickness = PasswordInput.BorderThickness, Foreground = PasswordInput.Foreground };
            _visiblePassword.TextChanged += (s, a) => { if (_isPasswordVisible && DataContext is LoginViewModel_v3 vm) vm.Password = _visiblePassword.Text; };
            _passwordToggle = new Button { Content = CreateEyeIcon(false), Width = 38, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(158, 180, 204)), BorderThickness = new Thickness(0), ToolTip = "Hiển thị mật khẩu" };
            _passwordToggle.Click += (s, a) => TogglePassword();
            // A WPF element can have only one logical parent: detach it before placing it in the host grid.
            parent.Children.RemoveAt(index);
            host.Children.Add(PasswordInput); host.Children.Add(_visiblePassword);
            host.Children.Add(new TextBlock
            {
                Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(96, 202, 255)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0), IsHitTestVisible = false
            });
            host.Children.Add(_passwordToggle);
            parent.Children.Insert(index, host);
            var remember = new CheckBox { Content = "Ghi nhớ đăng nhập", Foreground = new SolidColorBrush(Color.FromRgb(194, 211, 229)), Margin = new Thickness(0, 8, 0, 14), IsChecked = (DataContext as LoginViewModel_v3)?.IsRememberMe == true };
            remember.Checked += (s, a) => { if (DataContext is LoginViewModel_v3 vm) vm.IsRememberMe = true; };
            remember.Unchecked += (s, a) => { if (DataContext is LoginViewModel_v3 vm) vm.IsRememberMe = false; };
            parent.Children.Insert(index + 1, remember);
        }

        private void UpdateResponsiveLoginLayout()
        {
            if (LoginLayout == null || MarketingPanel == null || LoginCard == null) return;
            var compact = ActualWidth > 0 && ActualWidth < 1100;
            LoginLayout.Margin = compact ? new Thickness(32, 40, 32, 30) : new Thickness(126, 40, 76, 30);
            MarketingPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            LoginLayout.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            LoginLayout.ColumnDefinitions[1].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(650);
            LoginCard.Width = compact ? Math.Max(360, Math.Min(600, ActualWidth - 64)) : 600;
            LoginCard.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        }

        private static void UpdateApplicationMarketingText(DependencyObject root)
        {
            if (root == null) return;

            var text = root as TextBlock;
            if (text != null && !string.IsNullOrWhiteSpace(text.Text) &&
                text.Text.IndexOf("web", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // This is the desktop client, not the web portal.  Keep the
                // wording consistent even when the XAML was created from an
                // earlier web-oriented template.
                text.Text = text.FontSize >= 24
                    ? "Giám sát video chuyên nghiệp\n" +
                      "mạnh mẽ và linh hoạt\n" +
                      "trên ứng dụng VMS."
                    : "◉  Ứng dụng VMS";
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
                UpdateApplicationMarketingText(VisualTreeHelper.GetChild(root, index));
        }

        private static void NormalizeMarketingPanelBackground(DependencyObject root)
        {
            if (root == null) return;
            var border = root as Border;
            // Keep two distinct login panels, but make the marketing panel a
            // single stable colour instead of a gradient that appears to
            // shift independently while the window is resized.
            if (border != null && border.Background is LinearGradientBrush)
                border.Background = new SolidColorBrush(Color.FromRgb(11, 36, 65));

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
                NormalizeMarketingPanelBackground(VisualTreeHelper.GetChild(root, index));
        }

        private static void UpdatePlatformCaption(DependencyObject root)
        {
            if (root == null) return;
            var text = root as TextBlock;
            if (text != null && !string.IsNullOrWhiteSpace(text.Text) &&
                text.Text.IndexOf("Nền tảng VMS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text.Text = "◉  Nền tảng giám sát video";
                return;
            }
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
                UpdatePlatformCaption(VisualTreeHelper.GetChild(root, index));
        }
        private void LoginPage_v3_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var oldVm = e.OldValue as INotifyPropertyChanged;
            if (oldVm != null) oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            var newVm = e.NewValue as INotifyPropertyChanged;
            if (newVm != null) newVm.PropertyChanged += ViewModel_PropertyChanged;
        }
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsProfileSelectionVisible") ScheduleProfileLayout();
            if (e.PropertyName == "HasError" && DataContext is LoginViewModel_v3 viewModel && viewModel.HasError)
                PlayLoginErrorAnimation();
        }
        private void PlayLoginErrorAnimation()
        {
            if (LoginCardTranslate == null) return;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromPercent(0.2)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromPercent(0.45)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(0.7)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            LoginCardTranslate.BeginAnimation(TranslateTransform.XProperty, shake);
        }
        private void ScheduleProfileLayout()
        {
            Dispatcher.BeginInvoke(new Action(() => StyleProfileHeader(this)), DispatcherPriority.Loaded);
        }
        private static void StyleProfileHeader(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var profilePanel = child as Border;
                if (profilePanel != null && (profilePanel.Width == 720 || profilePanel.Width == 480))
                {
                    Grid.SetColumn(profilePanel, 1);
                    Grid.SetColumnSpan(profilePanel, 1);
                    profilePanel.Width = 480;
                    // Grow on a large screen while keeping safe margins in a small window.
                    var window = Window.GetWindow(root) ?? Window.GetWindow(profilePanel);
                    var availableHeight = window == null ? 620 : Math.Max(520, window.ActualHeight - 140);
                    profilePanel.Height = Math.Min(720, availableHeight);
                    profilePanel.MaxHeight = 720;
                    profilePanel.Padding = new Thickness(20);
                    var overlay = VisualTreeHelper.GetParent(profilePanel) as Border;
                    if (overlay != null)
                    {
                        Grid.SetColumn(overlay, 1);
                        Grid.SetColumnSpan(overlay, 1);
                        overlay.Background = Brushes.Transparent;
                        overlay.Padding = new Thickness(0);
                    }
                }
                var scrollBar = child as ScrollBar;
                if (scrollBar != null && scrollBar.Orientation == Orientation.Vertical)
                {
                    scrollBar.Width = 4;
                    scrollBar.MinWidth = 4;
                    scrollBar.Opacity = 0.55;
                }
                var profileItem = child as ListBoxItem;
                if (profileItem != null)
                {
                    profileItem.Padding = new Thickness(14, 10, 14, 10);
                    profileItem.Margin = new Thickness(0, 0, 0, 6);
                }
                var profileList = child as ListBox;
                if (profileList != null) profileList.Padding = new Thickness(0, 0, 12, 0);
                var profileAction = child as Button;
                if (profileAction != null && string.Equals(profileAction.Content as string, "◉  Vào hệ thống", StringComparison.Ordinal))
                    profileAction.Content = "Vào hệ thống  →";
                var text = child as TextBlock;
                if (text != null && (text.Text == "☼  Sáng" || text.Text == "VI"))
                {
                    var chrome = VisualTreeHelper.GetParent(text) as Border;
                    if (chrome != null) chrome.Visibility = Visibility.Collapsed;
                }
                else if (text != null && text.Text == "↪ Đăng xuất")
                {
                    var chrome = VisualTreeHelper.GetParent(text) as Border;
                    text.FontSize = 11;
                    if (chrome != null)
                    {
                        chrome.Background = new SolidColorBrush(Color.FromRgb(13, 35, 57));
                        chrome.BorderBrush = new SolidColorBrush(Color.FromRgb(31, 72, 108));
                        chrome.Padding = new Thickness(9, 5, 9, 5);
                        chrome.CornerRadius = new CornerRadius(7);
                        chrome.VerticalAlignment = VerticalAlignment.Center;
                        chrome.Height = 30;
                    }
                }
                StyleProfileHeader(child);
            }
        }
        private void TogglePassword()
        {
            _isPasswordVisible = !_isPasswordVisible;
            if (_isPasswordVisible) { _visiblePassword.Text = PasswordInput.Password; PasswordInput.Visibility = Visibility.Collapsed; _visiblePassword.Visibility = Visibility.Visible; _passwordToggle.Content = CreateEyeIcon(true); _passwordToggle.ToolTip = "Ẩn mật khẩu"; }
            else { PasswordInput.Password = _visiblePassword.Text; _visiblePassword.Visibility = Visibility.Collapsed; PasswordInput.Visibility = Visibility.Visible; _passwordToggle.Content = CreateEyeIcon(false); _passwordToggle.ToolTip = "Hiển thị mật khẩu"; }
        }
        private static UIElement CreateEyeIcon(bool crossedOut)
        {
            var color = new SolidColorBrush(Color.FromRgb(96, 202, 255));
            var icon = new Grid { Width = 18, Height = 18 };
            icon.Children.Add(new TextBlock { Text = "\uE890", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (crossedOut) icon.Children.Add(new Line { X1 = 2, Y1 = 16, X2 = 16, Y2 = 2, Stroke = color, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            return icon;
        }
        private void OnPageMouseUp(object sender, MouseButtonEventArgs e)
        {
            var text = e.OriginalSource as TextBlock;
            if (text != null && text.Text == "↪ Đăng xuất" && DataContext is LoginViewModel_v3 vm && vm.LogoutCommand.CanExecute(null)) vm.LogoutCommand.Execute(null);
        }
        private void UsernameInput_OnTextChanged(object sender, TextChangedEventArgs e) { UpdateLoginButtonState(); }
        private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LoginViewModel_v3;
            if (viewModel != null) viewModel.Password = PasswordInput.Password;
            UpdateLoginButtonState();
        }
        private void UpdateLoginButtonState()
        {
            if (LoginSubmitButton == null || UsernameInput == null || PasswordInput == null) return;
            var isReady = !string.IsNullOrWhiteSpace(UsernameInput.Text) && !string.IsNullOrWhiteSpace(PasswordInput.Password);
            LoginSubmitButton.IsEnabled = isReady;
            Brush buttonBackground = isReady
                ? (Brush)new LinearGradientBrush(Color.FromRgb(37, 99, 235), Color.FromRgb(14, 165, 233), new Point(0, 0), new Point(1, 0))
                : new SolidColorBrush(Color.FromRgb(16, 53, 93));
            LoginSubmitButton.Background = buttonBackground;
        }
        private void LoginSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LoginViewModel_v3;
            if (viewModel != null && viewModel.LoginCommand.CanExecute(null)) viewModel.LoginCommand.Execute(null);
        }
        public void ClearPassword() { PasswordInput.Clear(); if (_visiblePassword != null) _visiblePassword.Clear(); _isPasswordVisible = false; if (_visiblePassword != null) _visiblePassword.Visibility = Visibility.Collapsed; PasswordInput.Visibility = Visibility.Visible; }
    }
}
