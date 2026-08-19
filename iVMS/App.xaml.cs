using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.libs;
using V3SClient.ucs;
using V3SClient.window;

namespace V3SClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private  Mutex _mutex;
 
        public static bool IsRun { get; set; }
        protected override async void OnStartup(StartupEventArgs e)
        {
            RegisterGlobalExceptionLogging();
            string mutexName = "V3SClient_VMS";
            bool isCreatNew = false;

            try
            {
                _mutex = new Mutex(true, mutexName, out isCreatNew);
                if (isCreatNew)
                {
                    GlobalClass.Init();
      
                    // The isolated migrated executable always uses the migrated login flow.
                    // The legacy login remains available in the preserved source but is not
                    // selected by this deliverable.
                    var loginWindow = new LoginWindow_v3();
                    bool? dialogResult = loginWindow.ShowDialog(); // Chờ kết quả đăng nhập

                    if (dialogResult == true)
                    {
                        // Do not reset the user back to a default-size shell
                        // after sign-in. Preserve both normal geometry and
                        // the all-monitor fullscreen mode from login.
                        var loginBounds = loginWindow.WindowBoundsForNextShell;
                        var loginWasVirtualDesktop = loginWindow.IsVirtualDesktopMode;
                        var selectedProfile = loginWindow.SelectedProfile;
                        // Đăng nhập thành công, mở MainWindow
                        // Keep the migrated shell as the only startup shell for this copy.
                        var mainWindow = new ShellWindow_v3(deferInitialNavigation: true);
                        mainWindow.ApplyStartupWindowPlacement(loginBounds, loginWasVirtualDesktop);
                        MainWindow = mainWindow;
                        mainWindow.Show();
                        _ = CompleteStartupAsync(mainWindow, selectedProfile);
                    }
                    else
                    {
                        Shutdown(); 
                    }
                    // base.OnStartup(e);
                    IsRun = true;
                }
                else
                {
                    MessageBox.Show("Ứng dụng đang chạy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    Application.Current.Shutdown();
                }
            }
            catch (SqlException ex)
            {
                LoggerManager.LogException(ex, "Unhandled SQL exception during application startup");
                MessageBox.Show("Không thể kết nối máy chủ.\n Vui lòng kiểm tra lại.","Lỗi",MessageBoxButton.OK,MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Unhandled exception during application startup");
                MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace + "\n\n" + "Ứng dụng đang thoát...", "Lỗi hệ thống");
                Application.Current.Shutdown();
            }

        }
       
        private async Task CompleteStartupAsync(ShellWindow_v3 shell, ApiManager.ClientProfile profile)
        {
            try
            {
                // Paint the waiting state before starting network or native work.
                await Task.Yield();
                shell.ShellPage.ShowInitialLoading("Đang tải cấu hình client và danh sách camera...", "Đang vào hệ thống");
                // Metadata migration and cleanup can be slow on machines with
                // many local AI files. Warm it up off the UI thread while the
                // client request is in flight.
                var metadataWarmupTask = Task.Run(() => { var storage = MetaAIResultStorage.Instance; });
                // The shell is created before the selected client is loaded.
                // Resolve a missing selection from the authorized list instead
                // of allowing the header to remain on its default placeholder.
                if (profile == null)
                    profile = GlobalUserInfo.Instance.AuthorizedProfiles?.FirstOrDefault();
                if (profile == null)
                    throw new InvalidOperationException("Không xác định được client đã chọn sau khi đăng nhập.");
                using (var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None))
                {
                    startupTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var clientLoadTask = new Services.ClientSessionService().SwitchClientAsync(profile, startupTimeout.Token, prepareForStartup: true);
                    await Task.WhenAll(clientLoadTask, metadataWarmupTask);
                }
                shell.ShellPage.ShowInitialLoading("Đang chuẩn bị bộ phát video. Lần đầu trên máy này có thể mất vài giây...", "Đang vào hệ thống");
                var mediaRuntimeTask = ShellWindow_v3.EnsureGStreamerInitializedAsync();
                if (await Task.WhenAny(mediaRuntimeTask, Task.Delay(TimeSpan.FromSeconds(25))) != mediaRuntimeTask)
                    throw new TimeoutException("Khởi tạo bộ phát video mất quá lâu.");
                if (!await mediaRuntimeTask)
                    throw new InvalidOperationException("Không tìm thấy bộ phát video cần thiết.");
                shell.RefreshSessionDisplay();
                if (shell.IsVisible)
                    shell.CompleteInitialNavigation();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Unable to load selected client during shell startup.");
                if (shell.IsVisible)
                    shell.ShowInitialLoadFailure(
                        "Không thể hoàn tất khởi động. Vui lòng kiểm tra kết nối API hoặc bộ phát video rồi thử lại.",
                        () => _ = CompleteStartupAsync(shell, profile));
            }
        }

        private void RegisterGlobalExceptionLogging()
        {
            var diagnosticDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iVista VMS", "logs");
            System.IO.Directory.CreateDirectory(diagnosticDirectory);

            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            Dispatcher.UnhandledExceptionFilter += Dispatcher_UnhandledExceptionFilter;
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                LoggerManager.LogError(
                    "Unhandled AppDomain exception. IsTerminating: " + args.IsTerminating + "; Exception: " + args.ExceptionObject);
            };
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LoggerManager.LogException(args.Exception, "Unobserved background task exception");
            };
        }

        private void Dispatcher_UnhandledExceptionFilter(object sender, System.Windows.Threading.DispatcherUnhandledExceptionFilterEventArgs e)
        {
            LoggerManager.LogException(e.Exception, "Unhandled WPF dispatcher exception filter");
            e.RequestCatch = true;
        }

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LoggerManager.LogException(e.Exception, "Unhandled WPF dispatcher exception");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }
    }
}
