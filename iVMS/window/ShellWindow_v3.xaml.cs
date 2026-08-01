using System;
using System.Configuration;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using V3SClient.UI.Views;
using V3SClient.viewModels;
using V3SClient.libs;

namespace V3SClient.window
{
    public partial class ShellWindow_v3 : Window
    {
        // ShellView is hosted inside the rounded outer Border, therefore it
        // cannot be obtained through Window.Content by child pages.
        public ShellPage_v3 ShellPage { get { return ShellView; } }
        private readonly ShellViewModel_v3 _viewModel;
        private static bool _gstreamerInitialized;
        private bool _logoutRequested;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const int WsBorder = 0x00800000;
        private const int WsDlgFrame = 0x00400000;
        private const int WsExClientEdge = 0x00000200;
        private const int WsExWindowEdge = 0x00000100;
        private bool _isVirtualDesktopMode;
        private Rect _normalWindowBounds;
        private int _normalNativeStyle;
        private int _normalNativeExStyle;
        private bool _nativeFrameStyleSaved;
        private bool _startInVirtualDesktopMode;
        private const int WmNcHitTest = 0x0084;
        private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        public bool IsVirtualDesktopMode { get { return _isVirtualDesktopMode; } }

        /// <summary>
        /// Carries window geometry and multi-monitor fullscreen state across
        /// the login-to-shell transition. Fullscreen is applied after Loaded,
        /// when the native shell handle is ready.
        /// </summary>
        public void ApplyStartupWindowPlacement(Rect normalBounds, bool virtualDesktopMode)
        {
            if (normalBounds.Width > 0 && normalBounds.Height > 0)
            {
                _normalWindowBounds = normalBounds;
                Left = normalBounds.Left;
                Top = normalBounds.Top;
                Width = Math.Max(MinWidth, normalBounds.Width);
                Height = Math.Max(MinHeight, normalBounds.Height);
            }

            _startInVirtualDesktopMode = virtualDesktopMode;
            if (_startInVirtualDesktopMode)
                Loaded += ApplyStartupVirtualDesktopMode;
        }

        private void ApplyStartupVirtualDesktopMode(object sender, RoutedEventArgs e)
        {
            Loaded -= ApplyStartupVirtualDesktopMode;
            if (!_startInVirtualDesktopMode || _isVirtualDesktopMode)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isVirtualDesktopMode)
                    ToggleVirtualDesktopMode();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        public ShellWindow_v3()
        {
            InitializeComponent();
            // Keep the Live View usable when the shell is resized from a
            // corner. The minimum is 40% of the current work area, while the
            // XAML values provide a safe fallback before the window is shown.
            MinWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width * 0.40);
            MinHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height * 0.40);
            InitializeGStreamer_v3();
            _viewModel = new ShellViewModel_v3();
            DataContext = _viewModel;
            ShellView.DataContext = _viewModel;
            SourceInitialized += ShellWindow_SourceInitialized;
            Closed += (s, e) =>
            {
                _viewModel.Dispose();
                if (!_logoutRequested)
                    Application.Current.Shutdown();
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_logoutRequested && SmartDownloadManager.Instance.HasActiveDownloads)
            {
                var result = MessageBox.Show(
                    "Đang có video được tải xuống. Nếu thoát, các tác vụ tải đang chạy sẽ bị hủy. Bạn vẫn muốn thoát?",
                    "Xác nhận thoát ứng dụng",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                SmartDownloadManager.Instance.CancelAllActive();
            }

            base.OnClosing(e);
        }

        public void LogoutAndReturnToLogin()
        {
            if (_logoutRequested)
                return;

            _logoutRequested = true;
            Close();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var login = new LoginWindow_v3();
                Application.Current.MainWindow = login;
                if (login.ShowDialog() == true)
                {
                    var next = new ShellWindow_v3();
                    Application.Current.MainWindow = next;
                    next.Show();
                }
                else Application.Current.Shutdown();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static void InitializeGStreamer_v3()
        {
            if (_gstreamerInitialized)
                return;

            var bundledRuntimeRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64");
            var configuredRuntimeRoot = ConfigurationManager.AppSettings["GStreamerRoot_v3"];
            var installedRuntimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "gstreamer", "1.0", "msvc_x86_64");

            // The copied x64 folder in bin\Debug is not a distributable
            // GStreamer runtime (it only contains headers/pkgconfig after the
            // merge).  It must never replace a complete installed runtime,
            // otherwise Parse.Launch cannot find rtspsrc.
            var runtimeRoot = IsCompleteGStreamerRuntime(configuredRuntimeRoot)
                ? configuredRuntimeRoot
                : IsCompleteGStreamerRuntime(installedRuntimeRoot)
                    ? installedRuntimeRoot
                    : IsCompleteGStreamerRuntime(bundledRuntimeRoot)
                        ? bundledRuntimeRoot
                        : null;

            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                libs.LoggerManager.LogError(
                    "GStreamer runtime is incomplete: the RTSP plugin (gstrtsp.dll) was not found.", null);
                return;
            }
            var runtimeBin = Path.Combine(runtimeRoot, "bin");
            var pluginPath = Path.Combine(runtimeRoot, "lib", "gstreamer-1.0");
            var gioModulePath = Path.Combine(runtimeRoot, "lib", "gio", "modules");
            var pluginScanner = Path.Combine(runtimeRoot, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
            var diagnosticDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iVista VMS", "logs");
            var gstreamerLogPath = Path.Combine(diagnosticDirectory, "gstreamer.log");
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            // The installed app runs from Program Files, where a standard user
            // cannot reliably create relative log folders. Keep native playback
            // diagnostics in LocalAppData alongside the managed application log.
            Directory.CreateDirectory(diagnosticDirectory);
            Environment.SetEnvironmentVariable("GST_DEBUG", "*:2,souphttpsrc:4,hlsdemux:4", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GST_DEBUG_NO_COLOR", "1", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GST_DEBUG_FILE", gstreamerLogPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GST_PLUGIN_PATH", pluginPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GST_PLUGIN_SYSTEM_PATH_1_0", pluginPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GIO_MODULE_DIR", gioModulePath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GIO_EXTRA_MODULES", gioModulePath, EnvironmentVariableTarget.Process);
            if (File.Exists(pluginScanner))
                Environment.SetEnvironmentVariable("GST_PLUGIN_SCANNER_1_0", pluginScanner, EnvironmentVariableTarget.Process);
            if (!currentPath.StartsWith(runtimeBin + ";", StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", runtimeBin + ";" + currentPath, EnvironmentVariableTarget.Process);

            Gst.Application.Init();
            libs.LoggerManager.LogInfo("Live View _v3 GStreamer runtime: " + runtimeRoot);
            libs.LoggerManager.LogInfo("GStreamer diagnostics: " + gstreamerLogPath);
            _gstreamerInitialized = true;
        }
        private void ShellWindow_SourceInitialized(object sender, EventArgs e)
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowResizeHook);
        }
        private IntPtr WindowResizeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
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

        public void ToggleVirtualDesktopMode()
        {
            if (_isVirtualDesktopMode)
            {
                WindowState = WindowState.Normal;
                Left = _normalWindowBounds.Left;
                Top = _normalWindowBounds.Top;
                Width = _normalWindowBounds.Width;
                Height = _normalWindowBounds.Height;
                // A virtual wall strips the native thick frame. Restore the
                // WPF resize contract before notifying Windows that the frame
                // changed, otherwise the borderless shell can get stuck in a
                // non-resizable state after leaving fullscreen.
                _isVirtualDesktopMode = false;
                ResizeMode = ResizeMode.CanResize;
                var restoreHandle = new WindowInteropHelper(this).Handle;
                RestoreNativeWindowFrame(restoreHandle);
                SetWindowPos(restoreHandle, HwndNoTopmost,
                    0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
                UpdateLayout();
                return;
            }

            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
                _normalWindowBounds = bounds;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
            // Set the WPF geometry as well as the native HWND geometry. The
            // shell's layout pass otherwise restores its old single-monitor
            // Width/Height immediately after SetWindowPos.
            Left = virtualScreen.Left;
            Top = virtualScreen.Top;
            Width = virtualScreen.Width;
            Height = virtualScreen.Height;
            UpdateLayout();
            var handle = new WindowInteropHelper(this).Handle;
            SaveNativeWindowFrame(handle);
            RemoveNativeWindowFrame(handle);
            // A virtual desktop must span monitors, but it must not become a
            // global topmost window.  Topmost also promotes child Popup HWNDs
            // (camera IDs) above unrelated foreground applications.
            SetWindowPos(handle, HwndNoTopmost, virtualScreen.Left, virtualScreen.Top,
                virtualScreen.Width, virtualScreen.Height,
                SwpNoActivate | SwpFrameChanged);
            _isVirtualDesktopMode = true;
            LoggerManager.LogInfo(string.Format(
                "Live View virtual desktop enabled: {0},{1} {2}x{3}.",
                virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height));
        }

        /// <summary>
        /// Expands the current window over its current monitor, including the
        /// taskbar area, without promoting it to a global topmost window.
        /// LivePage restores the previous bounds when tile fullscreen ends.
        /// </summary>
        public void EnterCurrentScreenFullscreen()
        {
            if (_isVirtualDesktopMode)
                return;

            var handle = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var bounds = screen.Bounds;
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            UpdateLayout();
            SetWindowPos(handle, HwndTop, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpFrameChanged);
        }

        /// <summary>
        /// Keeps virtual-desktop mode enabled for later restoration, while a
        /// single selected camera occupies only the user's primary monitor.
        /// </summary>
        public void EnterPrimaryScreenPresentation()
        {
            if (!_isVirtualDesktopMode)
            {
                EnterCurrentScreenFullscreen();
                return;
            }

            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var handle = new WindowInteropHelper(this).Handle;
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            UpdateLayout();
            SetWindowPos(handle, HwndTop, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpFrameChanged);
        }

        /// <summary>Restores the two/multi-monitor wall after a single-camera view.</summary>
        public void RestoreVirtualDesktopPresentation()
        {
            if (!_isVirtualDesktopMode)
                return;

            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            var handle = new WindowInteropHelper(this).Handle;
            WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            UpdateLayout();
            SetWindowPos(handle, HwndNoTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpFrameChanged);
        }

        /// <summary>
        /// Mirrors the original MainWindow title-bar behavior: a drag from
        /// virtual-desktop mode first restores the normal bounds, then lets
        /// Windows move the window naturally.
        /// </summary>
        public void BeginMoveFromHeader()
        {
            if (_isVirtualDesktopMode)
                ToggleVirtualDesktopMode();

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // A button press can be cancelled while the shell is changing
                // geometry. There is nothing to move in that case.
            }
        }

        private static void RemoveNativeWindowFrame(IntPtr handle)
        {
            var style = GetWindowLong(handle, GwlStyle);
            style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
            SetWindowLong(handle, GwlStyle, style);
            var exStyle = GetWindowLong(handle, GwlExStyle);
            exStyle &= ~(WsExClientEdge | WsExWindowEdge);
            SetWindowLong(handle, GwlExStyle, exStyle);
        }

        private void SaveNativeWindowFrame(IntPtr handle)
        {
            if (_nativeFrameStyleSaved || handle == IntPtr.Zero) return;
            _normalNativeStyle = GetWindowLong(handle, GwlStyle);
            _normalNativeExStyle = GetWindowLong(handle, GwlExStyle);
            _nativeFrameStyleSaved = true;
        }

        private void RestoreNativeWindowFrame(IntPtr handle)
        {
            if (!_nativeFrameStyleSaved || handle == IntPtr.Zero) return;
            SetWindowLong(handle, GwlStyle, _normalNativeStyle);
            SetWindowLong(handle, GwlExStyle, _normalNativeExStyle);
            _nativeFrameStyleSaved = false;
        }

        private static bool IsCompleteGStreamerRuntime(string runtimeRoot)
        {
            if (string.IsNullOrWhiteSpace(runtimeRoot)) return false;
            return File.Exists(Path.Combine(runtimeRoot, "bin", "gstreamer-1.0-0.dll")) &&
                   File.Exists(Path.Combine(runtimeRoot, "lib", "gstreamer-1.0", "gstrtsp.dll"));
        }
    }
}
