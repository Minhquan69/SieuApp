using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.IO;
using IOPath = System.IO.Path;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class LoginPage_v3 : Page
    {
        private TextBox _visiblePassword;
        private Button _passwordToggle;
        private bool _isPasswordVisible;
        private bool _isLightLoginTheme;
        private bool _profileThemeHooked;
        private Border _visiblePasswordChrome;
        private readonly Dictionary<DependencyObject, Brush> _originalForegrounds = new Dictionary<DependencyObject, Brush>();
        private readonly Dictionary<DependencyObject, Brush> _originalBackgrounds = new Dictionary<DependencyObject, Brush>();
        public LoginPage_v3()
        {
            _isLightLoginTheme = LoadLoginThemePreference();
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
            UpdateLoginTitle(this);
            ApplyLoginThemeAssets();
            var cachedLogin = DataContext as LoginViewModel_v3;
            if (cachedLogin != null && string.IsNullOrEmpty(PasswordInput.Password) && !string.IsNullOrEmpty(cachedLogin.Password))
                PasswordInput.Password = cachedLogin.Password;
            UpdateLoginButtonState();
            UpdateLoginBusyVisuals();
            StartLoginAmbientAnimation();
            var cardContent = LoginCard == null ? null : LoginCard.Child as StackPanel;
            if (cardContent != null && cardContent.Tag == null)
            {
                var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                footer.Children.Add(new TextBlock { Text = "\uE713", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Color.FromRgb(143, 167, 190)), FontSize = 20, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
                footer.Children.Add(new TextBlock { Text = "Phiên bản " + GetType().Assembly.GetName().Version.ToString(3), Foreground = new SolidColorBrush(Color.FromRgb(175, 193, 209)), FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
                footer.Children.Add(new Border { Width = 1, Height = 22, Background = new SolidColorBrush(Color.FromRgb(54, 80, 107)), Margin = new Thickness(22, 0, 22, 0) });
                footer.Children.Add(new TextBlock { Text = "© 2026 iVista Tech", Foreground = new SolidColorBrush(Color.FromRgb(175, 193, 209)), FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
                cardContent.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(36, 63, 89)), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 20, 0, 0), Padding = new Thickness(0, 16, 0, 0), Child = footer });
                cardContent.Tag = footer;
            }
            var parent = VisualTreeHelper.GetParent(PasswordInput) as Panel;
            if (parent == null || _visiblePassword != null) return;
            var index = parent.Children.IndexOf(PasswordInput);
            var host = new Grid { Height = 56, Margin = PasswordInput.Margin };
            _visiblePasswordChrome = new Border { CornerRadius = new CornerRadius(9), Background = PasswordInput.Background, BorderBrush = PasswordInput.BorderBrush, BorderThickness = PasswordInput.BorderThickness };
            host.Children.Add(_visiblePasswordChrome);
            PasswordInput.Margin = new Thickness(0); PasswordInput.Padding = new Thickness(0); PasswordInput.VerticalContentAlignment = VerticalAlignment.Center;
            _visiblePassword = new TextBox { Visibility = Visibility.Collapsed, Height = 56, Margin = new Thickness(48, 0, 42, 0), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 20, Background = PasswordInput.Background, BorderBrush = PasswordInput.BorderBrush, BorderThickness = new Thickness(0), Foreground = PasswordInput.Foreground };
            _visiblePassword.Background = Brushes.Transparent;
            _visiblePassword.BorderBrush = Brushes.Transparent;
            _visiblePassword.BorderThickness = new Thickness(0);
            var passwordHint = new TextBlock { Text = "Nhập mật khẩu", Foreground = new SolidColorBrush(Color.FromRgb(130, 149, 170)), FontSize = 18, FontStyle = FontStyles.Italic, Margin = new Thickness(48, 0, 42, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            passwordHint.Visibility = string.IsNullOrEmpty(PasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
            PasswordInput.PasswordChanged += (s, a) => passwordHint.Visibility = string.IsNullOrEmpty(PasswordInput.Password) && !_isPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
            _visiblePassword.TextChanged += (s, a) => { if (_isPasswordVisible && DataContext is LoginViewModel_v3 vm) vm.Password = _visiblePassword.Text; };
            _passwordToggle = new Button { Content = CreateEyeIcon(false), Width = 38, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(158, 180, 204)), BorderThickness = new Thickness(0), ToolTip = "Hiển thị mật khẩu" };
            _passwordToggle.Click += (s, a) => TogglePassword();
            // A WPF element can have only one logical parent: detach it before placing it in the host grid.
            parent.Children.RemoveAt(index);
            host.Children.Add(PasswordInput); host.Children.Add(_visiblePassword); host.Children.Add(passwordHint);
            host.Children.Add(new TextBlock
            {
                Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(96, 202, 255)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0), IsHitTestVisible = false
            });
            host.Children.Add(_passwordToggle);
            parent.Children.Insert(index, host);
            var remember = new CheckBox { Content = "Ghi nhớ đăng nhập", Style = (Style)FindResource("LoginRememberCheckBox"), Margin = new Thickness(0, 8, 0, 14), IsChecked = (DataContext as LoginViewModel_v3)?.IsRememberMe == true };
            remember.Checked += (s, a) => { if (DataContext is LoginViewModel_v3 vm) vm.IsRememberMe = true; };
            remember.Unchecked += (s, a) => { if (DataContext is LoginViewModel_v3 vm) vm.IsRememberMe = false; };
            parent.Children.Insert(index + 1, remember);
            ApplyLoginThemeColors(LoginCard, _isLightLoginTheme);
            ApplyVisiblePasswordTheme();
            ApplyUsernameTheme();
            ApplyLoginErrorTheme();
            ApplyProfileTheme(LoginCard);
        }

        private void UpdateResponsiveLoginLayout()
        {
            if (LoginLayout == null || MarketingPanel == null || LoginCard == null) return;
            var compact = ActualWidth > 0 && ActualWidth < 1100;
            LoginLayout.Margin = compact ? new Thickness(32, 40, 32, 30) : new Thickness(96, 54, 76, 44);
            MarketingPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            LoginLayout.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(61, GridUnitType.Star);
            LoginLayout.ColumnDefinitions[1].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(39, GridUnitType.Star);
            LoginCard.Width = compact ? Math.Max(360, Math.Min(600, ActualWidth - 64)) : double.NaN;
            LoginCard.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        }

        private void StartLoginAmbientAnimation()
        {
            AnimateGlow(LoginCard, 0.36, 0.78, 2.2);
            AnimateGlow(LoginSubmitButton, 0.48, 0.9, 1.35);
        }

        private void LoginLogo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isLightLoginTheme = !_isLightLoginTheme;
            SaveLoginThemePreference(_isLightLoginTheme);
            LoginBackgroundImage.Source = new BitmapImage(new Uri(
                _isLightLoginTheme
                    ? "pack://application:,,,/Assets/Images/login_background_light.png"
                    : "pack://application:,,,/Assets/Images/ivms-login-background-3d-1920x1080.png",
                UriKind.Absolute));
            LoginLogo.Source = new BitmapImage(new Uri(
                _isLightLoginTheme
                    ? "pack://application:,,,/images/logo/ivista_logo.png"
                    : "pack://application:,,,/images/logo/logo_1.png",
                UriKind.Absolute));
            LoginCard.BorderBrush = new SolidColorBrush(_isLightLoginTheme
                ? Color.FromRgb(255, 159, 67)
                : Color.FromArgb(185, 80, 216, 255));
            var cardGlow = LoginCard.Effect as DropShadowEffect;
            if (cardGlow != null)
            {
                if (cardGlow.IsFrozen) cardGlow = cardGlow.Clone();
                cardGlow.Color = _isLightLoginTheme
                    ? Color.FromRgb(255, 159, 67)
                    : Color.FromRgb(18, 156, 255);
                LoginCard.Effect = cardGlow;
            }
            ApplyLoginThemeColors(LoginCard, _isLightLoginTheme);
            ApplyVisiblePasswordTheme();
            ApplyUsernameTheme();
            ApplyProfileTheme(LoginCard);
            e.Handled = true;
        }

        private void ApplyLoginThemeAssets()
        {
            LoginBackgroundImage.Source = new BitmapImage(new Uri(
                _isLightLoginTheme
                    ? "pack://application:,,,/Assets/Images/login_background_light.png"
                    : "pack://application:,,,/Assets/Images/ivms-login-background-3d-1920x1080.png",
                UriKind.Absolute));
            LoginLogo.Source = new BitmapImage(new Uri(
                _isLightLoginTheme
                    ? "pack://application:,,,/images/logo/ivista_logo.png"
                    : "pack://application:,,,/images/logo/logo_1.png",
                UriKind.Absolute));
            LoginCard.BorderBrush = new SolidColorBrush(_isLightLoginTheme
                ? Color.FromRgb(255, 159, 67)
                : Color.FromArgb(185, 80, 216, 255));
            if (LoginCard.Effect is DropShadowEffect glow)
            {
                if (glow.IsFrozen) glow = glow.Clone();
                glow.Color = _isLightLoginTheme ? Color.FromRgb(255, 159, 67) : Color.FromRgb(18, 156, 255);
                LoginCard.Effect = glow;
            }
            ApplyLoginErrorTheme();
        }

        private void ApplyLoginErrorTheme()
        {
            if (LoginErrorBanner == null || LoginErrorText == null) return;

            LoginErrorBanner.Background = new SolidColorBrush(_isLightLoginTheme
                ? Color.FromRgb(255, 248, 245)
                : Color.FromArgb(77, 91, 20, 32));
            LoginErrorBanner.BorderBrush = new SolidColorBrush(_isLightLoginTheme
                ? Color.FromRgb(205, 166, 157)
                : Color.FromArgb(217, 91, 36, 48));
            LoginErrorText.Foreground = new SolidColorBrush(_isLightLoginTheme
                ? Color.FromRgb(91, 67, 62)
                : Color.FromRgb(229, 232, 232));
            LoginErrorText.FontSize = _isLightLoginTheme ? 14 : 18;
        }

        private static string GetLoginThemePreferencePath()
        {
            var directory = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iVista VMS");
            Directory.CreateDirectory(directory);
            return IOPath.Combine(directory, "login-theme.tmp");
        }
        private static bool LoadLoginThemePreference()
        {
            try { return File.Exists(GetLoginThemePreferencePath()) && File.ReadAllText(GetLoginThemePreferencePath()) == "light"; }
            catch { return false; }
        }
        private static void SaveLoginThemePreference(bool light)
        {
            try { File.WriteAllText(GetLoginThemePreferencePath(), light ? "light" : "dark"); }
            catch { }
        }

        private void ApplyProfileTheme(DependencyObject root)
        {
            if (root == null) return;
            if (root is ComboBox combo)
            {
                combo.Background = _isLightLoginTheme ? Brushes.White : new SolidColorBrush(Color.FromArgb(179, 17, 38, 61));
                combo.BorderBrush = _isLightLoginTheme ? new SolidColorBrush(Color.FromRgb(216, 222, 232)) : new SolidColorBrush(Color.FromRgb(73, 110, 151));
                combo.Foreground = _isLightLoginTheme ? Brushes.Black : new SolidColorBrush(Color.FromRgb(234, 242, 255));
                foreach (var item in combo.Items)
                {
                    if (combo.ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem comboItem)
                    {
                        comboItem.Foreground = item == combo.SelectedItem
                            ? Brushes.White
                            : (_isLightLoginTheme ? Brushes.Black : new SolidColorBrush(Color.FromRgb(234, 242, 255)));
                        comboItem.Background = _isLightLoginTheme ? Brushes.Transparent : Brushes.Transparent;
                    }
                }
                if (!_profileThemeHooked)
                {
                    _profileThemeHooked = true;
                    combo.DropDownOpened += (s, e) => Dispatcher.BeginInvoke(new Action(() => ApplyProfileTheme(combo)), DispatcherPriority.Loaded);
                }
                if (combo.Template.FindName("PART_Popup", combo) is Popup popup && popup.Child is Border popupBorder)
                {
                    popupBorder.Background = _isLightLoginTheme ? Brushes.White : new SolidColorBrush(Color.FromRgb(15, 38, 61));
                    popupBorder.BorderBrush = _isLightLoginTheme ? new SolidColorBrush(Color.FromRgb(216, 222, 232)) : new SolidColorBrush(Color.FromRgb(79, 127, 168));
                }
            }
            if (root is Border comboBorder && FindVisualAncestor<ComboBox>(comboBorder) != null)
            {
                comboBorder.Background = _isLightLoginTheme ? Brushes.White : new SolidColorBrush(Color.FromArgb(179, 17, 38, 61));
                comboBorder.BorderBrush = _isLightLoginTheme ? new SolidColorBrush(Color.FromRgb(216, 222, 232)) : new SolidColorBrush(Color.FromRgb(73, 110, 151));
            }
            if (root is Button button && (button.Content as string)?.Contains("QUAY") == true)
            {
                button.Background = _isLightLoginTheme ? Brushes.White : new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
                button.BorderBrush = _isLightLoginTheme ? new SolidColorBrush(Color.FromRgb(216, 222, 232)) : new SolidColorBrush(Color.FromArgb(100, 127, 164, 194));
                button.Foreground = _isLightLoginTheme ? Brushes.Black : new SolidColorBrush(Color.FromRgb(234, 242, 255));
            }
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++) ApplyProfileTheme(VisualTreeHelper.GetChild(root, i));
        }

        private static T FindVisualAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is T match) return match;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void ApplyUsernameTheme()
        {
            if (UsernameInputFrame == null) return;
            UsernameInputFrame.Background = _isLightLoginTheme
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(167, 6, 19, 36));
            UsernameInputFrame.BorderBrush = _isLightLoginTheme
                ? new SolidColorBrush(Color.FromRgb(216, 222, 232))
                : new SolidColorBrush(Color.FromArgb(74, 82, 102, 125));
        }

        private void ApplyVisiblePasswordTheme()
        {
            if (_visiblePassword == null) return;
            var passwordBackground = _isLightLoginTheme
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(167, 6, 19, 36));
            var passwordBorder = _isLightLoginTheme
                ? new SolidColorBrush(Color.FromRgb(216, 222, 232))
                : new SolidColorBrush(Color.FromArgb(74, 82, 102, 125));
            PasswordInput.Background = passwordBackground;
            PasswordInput.BorderBrush = passwordBorder;
            PasswordInput.Foreground = _isLightLoginTheme ? Brushes.Black : new SolidColorBrush(Color.FromRgb(217, 229, 241));
            _visiblePassword.Background = Brushes.Transparent;
            _visiblePassword.BorderBrush = Brushes.Transparent;
            _visiblePassword.Foreground = _isLightLoginTheme ? Brushes.Black : PasswordInput.Foreground;
            if (_visiblePasswordChrome != null)
            {
                _visiblePasswordChrome.Background = passwordBackground;
                _visiblePasswordChrome.BorderBrush = passwordBorder;
            }
        }

        private void ApplyLoginThemeColors(DependencyObject root, bool light)
        {
            if (root == null) return;
            // These controls have their own theme pass. Excluding them here prevents
            // the recursive color pass from restoring a stale brush on later toggles.
            if (root == PasswordInput || root == _visiblePassword || root == _visiblePasswordChrome)
                return;
            if (root == LoginCard)
            {
                if (!_originalBackgrounds.ContainsKey(root)) _originalBackgrounds[root] = LoginCard.Background;
                LoginCard.Background = light ? new SolidColorBrush(Color.FromRgb(247, 249, 252)) : _originalBackgrounds[root];
            }
            if (root is TextBlock text && !IsInsideButton(text))
            {
                if (!_originalForegrounds.ContainsKey(text)) _originalForegrounds[text] = text.Foreground;
                text.Foreground = text.Text == "\uE72E"
                    ? new SolidColorBrush(Color.FromRgb(96, 202, 255))
                    : (light ? Brushes.Black : _originalForegrounds[text]);
            }
            if (root is TextBox textBox)
            {
                if (!_originalForegrounds.ContainsKey(textBox)) _originalForegrounds[textBox] = textBox.Foreground;
                textBox.Foreground = light ? Brushes.Black : _originalForegrounds[textBox];
            }
            if (root is PasswordBox passwordBox)
            {
                if (!_originalForegrounds.ContainsKey(passwordBox)) _originalForegrounds[passwordBox] = passwordBox.Foreground;
                if (!_originalBackgrounds.ContainsKey(passwordBox)) _originalBackgrounds[passwordBox] = passwordBox.Background;
                passwordBox.Foreground = light ? Brushes.Black : _originalForegrounds[passwordBox];
                passwordBox.Background = light ? Brushes.White : _originalBackgrounds[passwordBox];
            }
            if (root is Border border && border != LoginCard && border.Background is SolidColorBrush background && background.Color.B < 90)
            {
                if (!_originalBackgrounds.ContainsKey(border)) _originalBackgrounds[border] = border.Background;
                border.Background = light ? Brushes.White : _originalBackgrounds[border];
            }
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++) ApplyLoginThemeColors(VisualTreeHelper.GetChild(root, i), light);
        }

        private static bool IsInsideButton(DependencyObject element)
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is Button) return true;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        private static void AnimateGlow(UIElement element, double from, double to, double durationSeconds)
        {
            if (element == null) return;
            var effect = element.Effect as DropShadowEffect;
            if (effect == null) return;
            if (effect.IsFrozen)
            {
                effect = effect.Clone();
                element.Effect = effect;
            }
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(durationSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
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

        private static void UpdateLoginTitle(DependencyObject root)
        {
            if (root == null) return;
            var text = root as TextBlock;
            if (text != null && text.FontSize >= 26 && text.FontWeight == FontWeights.ExtraBold)
                text.Text = "ĐĂNG NHẬP";
            else if (text != null && text.FontSize == 14 && text.HorizontalAlignment == HorizontalAlignment.Center)
                text.Visibility = Visibility.Collapsed;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
                UpdateLoginTitle(VisualTreeHelper.GetChild(root, index));
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
            if (e.PropertyName == "IsProfileSelectionVisible")
            {
                ScheduleProfileLayout();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyLoginThemeColors(LoginCard, _isLightLoginTheme);
                    ApplyVisiblePasswordTheme();
                    ApplyUsernameTheme();
                    ApplyProfileTheme(LoginCard);
                }), DispatcherPriority.Loaded);
            }
            if (e.PropertyName == "IsBusy") UpdateLoginBusyVisuals();
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
        private void StyleProfileHeader(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var profilePanel = child as Border;
                if (profilePanel != null && !ReferenceEquals(profilePanel, LoginCard) && (profilePanel.Width == 720 || profilePanel.Width == 480))
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
            ApplyVisiblePasswordTheme();
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

        private void UpdateLoginBusyVisuals()
        {
            var isBusy = (DataContext as LoginViewModel_v3)?.IsBusy == true;
            if (LoginSubmitText != null)
                LoginSubmitText.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
            if (LoginSubmitArrow != null)
                LoginSubmitArrow.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
            if (LoginBusyIndicator != null)
                LoginBusyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (LoginSubmitButton != null)
                LoginSubmitButton.IsHitTestVisible = !isBusy;
        }
        private void LoginSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LoginViewModel_v3;
            if (viewModel != null && viewModel.LoginCommand.CanExecute(null)) viewModel.LoginCommand.Execute(null);
        }
        public void ClearPassword() { PasswordInput.Clear(); if (_visiblePassword != null) _visiblePassword.Clear(); _isPasswordVisible = false; if (_visiblePassword != null) _visiblePassword.Visibility = Visibility.Collapsed; PasswordInput.Visibility = Visibility.Visible; }
    }
}
