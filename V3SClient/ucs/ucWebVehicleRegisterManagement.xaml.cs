using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for ucWebVehicleRegisterManagement.xaml
    /// </summary>
    public partial class ucWebVehicleRegisterManagement : UserControl
    {
        private bool _isWebViewInitialized = false;

        public ucWebVehicleRegisterManagement()
        {
            InitializeComponent();
            this.Loaded += UcWebVehicleRegisterManagement_Loaded;
        }

        private async void UcWebVehicleRegisterManagement_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isWebViewInitialized) return; // Tránh khởi tạo lại nhiều lần
            await InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                // Khởi tạo môi trường WebView2 
                var env = await V3SClient.libs.WebViewEnvHelper.GetSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Điều hướng tới địa chỉ web live
                string linkDirect = ApiManager.Instance.GetEndpointUrl("_vehicleReg");
                webView.Source = new Uri(linkDirect);
                _isWebViewInitialized = true;
                 //btnClearCache_Click();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cần cài đặt Web Runtime để hiển thị trang web.\n" + ex.Message, "Lỗi Web runtime", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Xóa toàn bộ cookie, cache, localStorage của WebView2 để đăng nhập lại
        /// </summary>
        private async void btnClearCache_Click()
        {
            try
            {
                if (webView.CoreWebView2 == null) return;

                // Xóa tất cả browsing data (cookies, cache, localStorage, sessionStorage...)
                await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();

                // Reload lại trang
                webView.CoreWebView2.Reload();

                Debug.WriteLine("Đã xóa dữ liệu đăng nhập cũ.\nTrang web sẽ được tải lại.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Không thể xóa cache: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
