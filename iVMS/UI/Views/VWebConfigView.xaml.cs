using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for VWebConfigView.xaml
    /// </summary>
    public partial class VWebConfigView : UserControl
    {
        private bool _isInitialized = false;
        private string _pendingRoute = null;

        public VWebConfigView()
        {
            InitializeComponent();
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                LoggerManager.LogDebug("Initializing WebView2...");
                // Set background color to match C# Client early
                webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(29, 23, 17); // #1D1711
                
                var env = await V3SClient.libs.WebViewEnvHelper.GetSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(env);
                
                // Disable DevTools and Context Menus for security
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Pre-inject configuration for authentication and layout
                await UpdatePreInjectionScript();
                
                webView.NavigationCompleted += WebView_NavigationCompleted;
                
                _isInitialized = true;
                LoggerManager.LogInfo("WebView2 initialized successfully.");

                if (!string.IsNullOrEmpty(_pendingRoute))
                {
                    NavigateTo(_pendingRoute);
                    _pendingRoute = null;
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Failed to initialize WebView2");
                MessageBox.Show("Không thể khởi tạo trình duyệt nhúng. Vui lòng đảm bảo đã cài đặt WebView2 Runtime.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string _lastScriptId = null;
        private async Task UpdatePreInjectionScript()
        {
            try
            {
                if (webView.CoreWebView2 == null) return;

                // Remove previous script if exists
                if (!string.IsNullOrEmpty(_lastScriptId))
                {
                    webView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_lastScriptId);
                }

                string token = ApiManager.Instance.BackendToken;
                var config = new
                {
                    token = token,
                    isEmbedded = true,
                    timestamp = DateTime.Now.Ticks // Cache busting
                };

                string jsonConfig = JsonConvert.SerializeObject(config);
                string script = $@"
                    window.IVISTA_CONFIG = {jsonConfig};
                    console.log('IVISTA_CONFIG injected by C# Client');
                ";

                _lastScriptId = await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                LoggerManager.LogDebug("Pre-injection script updated.");
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Error updating pre-injection script");
            }
        }

        private async void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                LoggerManager.LogDebug("WebView navigation completed. Injecting auth token...");
                await InjectAuthToken();
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                LoggerManager.LogWarn($"WebView navigation failed with stats: {e.WebErrorStatus}");
            }
        }

        private async Task InjectAuthToken()
        {
            try
            {
                string token = ApiManager.Instance.BackendToken;
                if (string.IsNullOrEmpty(token))
                {
                    LoggerManager.LogWarn("No backend token available to inject into WebView.");
                    return;
                }

                var authData = new { access_token = token };
                string jsonAuth = JsonConvert.SerializeObject(authData);
                
                // Call the global bridge function in Frontend
                string script = $"if (window.__IVISTA_SET_AUTH__) {{ window.__IVISTA_SET_AUTH__({jsonAuth}); }} else {{ console.warn('Bridge function __IVISTA_SET_AUTH__ not found'); }}";
                await webView.ExecuteScriptAsync(script);
                LoggerManager.LogDebug("Auth token injected successfully.");
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Error injecting auth token into WebView");
            }
        }

        public void NavigateTo(string route)
        {
            if (!_isInitialized)
            {
                _pendingRoute = route;
                return;
            }

            loadingOverlay.Visibility = Visibility.Visible;
            
            string baseUrl = ApiManager.Instance.BaseUrl;
            // Ensure trailing slash removed and route starts with slash
            baseUrl = baseUrl.TrimEnd('/');
            if (!route.StartsWith("/")) route = "/" + route;

            // Add embedded flag
            string separator = route.Contains("?") ? "&" : "?";
            string fullUrl = $"{baseUrl}{route}{separator}embedded=true";

            LoggerManager.LogInfo($"WebView navigating to: {fullUrl}");
            webView.Source = new Uri(fullUrl);
        }
    }
}
