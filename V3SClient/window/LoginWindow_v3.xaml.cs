using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using MahApps.Metro.IconPacks;
using V3SClient.UI.Views;
using V3SClient.viewModels;

namespace V3SClient.window
{
    public partial class LoginWindow_v3 : Window
    {
        private readonly LoginViewModel_v3 _viewModel;
        private readonly LoginPage_v3 _loginPage;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);
        private bool _isVirtualDesktopMode;
        private Rect _normalWindowBounds;
        private const int WmNcHitTest = 0x0084;
        private const int WmSizing = 0x0214;
        private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
        private const int WmszLeft = 1, WmszRight = 2, WmszTop = 3, WmszTopLeft = 4,
            WmszTopRight = 5, WmszBottom = 6, WmszBottomLeft = 7, WmszBottomRight = 8;
        private const double LoginAspectRatio = 1280d / 760d;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
        public LoginWindow_v3()
        {
            InitializeComponent();
            _viewModel = new LoginViewModel_v3();
            _viewModel.AuthenticationCompleted += ViewModel_AuthenticationCompleted;
            _viewModel.LoginResetRequested += ViewModel_LoginResetRequested;
            DataContext = _viewModel;
            // Navigated pages do not inherit the Window DataContext through a
            // Frame. Set it explicitly before Loaded so command bindings and
            // the cached-password setup are available immediately.
            _loginPage = new LoginPage_v3 { DataContext = _viewModel };
            LoginFrame.Navigate(_loginPage);
            Closed += LoginWindow_v3_Closed;
            SourceInitialized += LoginWindow_SourceInitialized;
        }
        private void LoginWindow_SourceInitialized(object sender, EventArgs e)
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowResizeHook);
        }
        private IntPtr WindowResizeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmSizing && !_isVirtualDesktopMode && ResizeMode != ResizeMode.NoResize)
            {
                ConstrainResizeToLoginAspect(wParam.ToInt32(), lParam);
                handled = true;
                return new IntPtr(1);
            }
            if (message != WmNcHitTest || _isVirtualDesktopMode || ResizeMode == ResizeMode.NoResize || !GetWindowRect(hwnd, out var bounds))
                return IntPtr.Zero;
            var point = lParam.ToInt64();
            var x = unchecked((short)(point & 0xffff));
            var y = unchecked((short)((point >> 16) & 0xffff));
            const int edge = 8;
            var left = x < bounds.Left + edge; var right = x >= bounds.Right - edge;
            var top = y < bounds.Top + edge; var bottom = y >= bounds.Bottom - edge;
            int hit = top ? (left ? HtTopLeft : right ? HtTopRight : HtTop) : bottom ? (left ? HtBottomLeft : right ? HtBottomRight : HtBottom) : left ? HtLeft : right ? HtRight : 0;
            if (hit == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(hit);
        }

        private static void ConstrainResizeToLoginAspect(int resizeEdge, IntPtr rectanglePointer)
        {
            if (rectanglePointer == IntPtr.Zero) return;
            var bounds = (NativeRect)Marshal.PtrToStructure(rectanglePointer, typeof(NativeRect));
            var width = Math.Max(1, bounds.Right - bounds.Left);
            var height = Math.Max(1, bounds.Bottom - bounds.Top);
            var horizontalEdge = resizeEdge == WmszLeft || resizeEdge == WmszRight ||
                                 resizeEdge == WmszTopLeft || resizeEdge == WmszTopRight ||
                                 resizeEdge == WmszBottomLeft || resizeEdge == WmszBottomRight;
            var verticalEdge = resizeEdge == WmszTop || resizeEdge == WmszBottom;

            if (verticalEdge)
            {
                width = (int)Math.Round(height * LoginAspectRatio);
                var centerX = (bounds.Left + bounds.Right) / 2;
                bounds.Left = centerX - width / 2;
                bounds.Right = bounds.Left + width;
            }
            else
            {
                height = (int)Math.Round(width / LoginAspectRatio);
                if (resizeEdge == WmszTop || resizeEdge == WmszTopLeft || resizeEdge == WmszTopRight)
                    bounds.Top = bounds.Bottom - height;
                else if (resizeEdge == WmszBottom || resizeEdge == WmszBottomLeft || resizeEdge == WmszBottomRight)
                    bounds.Bottom = bounds.Top + height;
                else
                {
                    var centerY = (bounds.Top + bounds.Bottom) / 2;
                    bounds.Top = centerY - height / 2;
                    bounds.Bottom = bounds.Top + height;
                }
            }

            // Corner drags should follow the dimension with the larger change;
            // width is the natural master dimension for this wide login view.
            if (horizontalEdge && !verticalEdge)
            {
                // Height was already adjusted from width above.
            }
            Marshal.StructureToPtr(bounds, rectanglePointer, false);
        }
        private void ViewModel_AuthenticationCompleted(object sender, EventArgs e) { _loginPage.ClearPassword(); DialogResult = true; Close(); }
        private void ViewModel_LoginResetRequested(object sender, EventArgs e) { _loginPage.ClearPassword(); }
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void ToggleWindowState_Click(object sender, RoutedEventArgs e)
        {
            ToggleVirtualDesktopMode();
        }
        private void CloseApplication_Click(object sender, RoutedEventArgs e) { Close(); }
        private void WindowChrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // The top 48px is the custom title bar. Keep its buttons clickable
            // while allowing normal window movement from the empty area.
            if (e.GetPosition(this).Y > 48) return;
            for (DependencyObject source = e.OriginalSource as DependencyObject;
                 source != null;
                 source = VisualTreeHelper.GetParent(source))
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase)
                    return;
            }

            if (e.ClickCount == 2)
            {
                ToggleVirtualDesktopMode();
                return;
            }

            if (_isVirtualDesktopMode)
                ToggleVirtualDesktopMode();

            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        private void ToggleVirtualDesktopMode()
        {
            if (_isVirtualDesktopMode)
            {
                WindowState = WindowState.Normal;
                Left = _normalWindowBounds.Left;
                Top = _normalWindowBounds.Top;
                Width = _normalWindowBounds.Width;
                Height = _normalWindowBounds.Height;
                SetWindowPos(new WindowInteropHelper(this).Handle, HwndNoTopmost,
                    0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
                _isVirtualDesktopMode = false;
                UpdateWindowModeIcon();
                return;
            }

            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
                _normalWindowBounds = bounds;

            WindowState = WindowState.Normal;
            var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
            Left = virtualScreen.Left;
            Top = virtualScreen.Top;
            Width = virtualScreen.Width;
            Height = virtualScreen.Height;
            UpdateLayout();
            // HWND_TOPMOST is intentional here.  A regular borderless WPF
            // window cannot cover the taskbar on a secondary monitor; this
            // is the fullscreen behavior expected by the original client.
            SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost,
                virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height,
                SwpNoActivate | SwpFrameChanged);
            _isVirtualDesktopMode = true;
            UpdateWindowModeIcon();
        }

        private void UpdateWindowModeIcon()
        {
            WindowModeIcon.Kind = _isVirtualDesktopMode
                ? PackIconMaterialKind.WindowRestore
                : PackIconMaterialKind.WindowMaximize;
        }
        private void LoginWindow_v3_Closed(object sender, EventArgs e) { _viewModel.AuthenticationCompleted -= ViewModel_AuthenticationCompleted; _viewModel.LoginResetRequested -= ViewModel_LoginResetRequested; _viewModel.Dispose(); }
    }
}
