using System;
using System.Configuration;
using System.IO;
using System.Windows;
using V3SClient.UI.Views;
using V3SClient.viewModels;
using V3SClient.libs;

namespace V3SClient.window
{
    public partial class ShellWindow_v3 : Window
    {
        private readonly ShellViewModel_v3 _viewModel;
        private static bool _gstreamerInitialized;
        private bool _logoutRequested;

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
            Closed += (s, e) => _viewModel.Dispose();
        }

        public void LogoutAndReturnToLogin()
        {
            _logoutRequested = true;
            Close();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var login = new LoginWindow();
                if (login.ShowDialog() == true)
                {
                    var next = new ShellWindow_v3();
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
            var runtimeRoot = !string.IsNullOrWhiteSpace(configuredRuntimeRoot) &&
                              Directory.Exists(Path.Combine(configuredRuntimeRoot, "bin")) &&
                              Directory.Exists(Path.Combine(configuredRuntimeRoot, "lib", "gstreamer-1.0"))
                ? configuredRuntimeRoot
                : bundledRuntimeRoot;
            var runtimeBin = Path.Combine(runtimeRoot, "bin");
            var pluginPath = Path.Combine(runtimeRoot, "lib", "gstreamer-1.0");
            var pluginScanner = Path.Combine(runtimeRoot, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            Environment.SetEnvironmentVariable("GST_PLUGIN_PATH", pluginPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GST_PLUGIN_SYSTEM_PATH_1_0", pluginPath, EnvironmentVariableTarget.Process);
            if (File.Exists(pluginScanner))
                Environment.SetEnvironmentVariable("GST_PLUGIN_SCANNER_1_0", pluginScanner, EnvironmentVariableTarget.Process);
            if (!currentPath.StartsWith(runtimeBin + ";", StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", runtimeBin + ";" + currentPath, EnvironmentVariableTarget.Process);

            Gst.Application.Init();
            libs.LoggerManager.LogInfo("Live View _v3 GStreamer runtime: " + runtimeRoot);
            _gstreamerInitialized = true;
        }
    }
}
