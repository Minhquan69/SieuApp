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
            
            string mutexName = "V3SClient";
            bool isCreatNew = false;

            try
            {
                _mutex = new Mutex(true, mutexName, out isCreatNew);
                if (isCreatNew)
                {
                    GlobalClass.Init();
      
                    bool useLoginV3;
                    bool.TryParse(ConfigurationManager.AppSettings["UseLoginV3"], out useLoginV3);
                    Window loginWindow = useLoginV3
                        ? (Window)new LoginWindow_v3()
                        : new LoginWindow();
                    bool? dialogResult = loginWindow.ShowDialog(); // Chờ kết quả đăng nhập

                    if (dialogResult == true)
                    {
                        GlobalSystem.Instance.Init();
                        MetaAIResultStorage.Instance.ToString();
                        // Đăng nhập thành công, mở MainWindow
                        bool useShellV3;
                        bool.TryParse(ConfigurationManager.AppSettings["UseShellV3"], out useShellV3);
                        Window mainWindow = useShellV3
                            ? (Window)new ShellWindow_v3()
                            : new MainWindow();
                        mainWindow.Show();
                    }
                    else
                    {
                        Shutdown(); 
                    }
                    // base.OnStartup(e);
                    IsRun = true;
                    Dispatcher.UnhandledException += Dispatcher_UnhandledException;
                    Dispatcher.UnhandledExceptionFilter += Dispatcher_UnhandledExceptionFilter;
                   
                }
                else
                {
                    MessageBox.Show("Ứng dụng đang chạy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    Application.Current.Shutdown();
                }
            }
            catch (SqlException ex)
            {
                MessageBox.Show("Không thể kết nối máy chủ.\n Vui lòng kiểm tra lại.","Lỗi",MessageBoxButton.OK,MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace + "\n\n" + "Ứng dụng đang thoát...", "Lỗi hệ thống");
                Application.Current.Shutdown();
            }

        }
       
        private void Dispatcher_UnhandledExceptionFilter(object sender, System.Windows.Threading.DispatcherUnhandledExceptionFilterEventArgs e)
        {
            e.RequestCatch = true;
        }

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {

        }
    }
}
