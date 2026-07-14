using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public static class WebViewEnvHelper
    {
        private static CoreWebView2Environment _sharedEnv;
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
                        _sharedEnv = await CoreWebView2Environment.CreateAsync(null, "Cache");
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            return _sharedEnv;
        }
    }
}
