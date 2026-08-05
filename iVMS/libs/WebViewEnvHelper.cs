using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public static class WebViewEnvHelper
    {
        private static CoreWebView2Environment _sharedEnv;
        private static CoreWebView2Environment _softwareMapEnv;
        private static readonly System.Threading.SemaphoreSlim _semaphore = new System.Threading.SemaphoreSlim(1, 1);

        public static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            if (_sharedEnv == null)
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (_sharedEnv == null)
                    {
                        var userDataFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "iVista VMS",
                            "WebView2");
                        Directory.CreateDirectory(userDataFolder);
                        _sharedEnv = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            return _sharedEnv;
        }

        /// <summary>
        /// A separate WebView2 environment for the embedded dashboard map.
        /// It avoids sharing the GPU compositor with multiple native live
        /// camera surfaces, while leaving every existing WebView untouched.
        /// </summary>
        public static async Task<CoreWebView2Environment> GetSoftwareMapEnvironmentAsync()
        {
            if (_softwareMapEnv == null)
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (_softwareMapEnv == null)
                    {
                        var folder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "iVista VMS",
                            "WebView2-MapSoftware");
                        Directory.CreateDirectory(folder);
                        var options = new CoreWebView2EnvironmentOptions("--disable-gpu --disable-gpu-compositing");
                        _softwareMapEnv = await CoreWebView2Environment.CreateAsync(null, folder, options);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            return _softwareMapEnv;
        }
    }
}
