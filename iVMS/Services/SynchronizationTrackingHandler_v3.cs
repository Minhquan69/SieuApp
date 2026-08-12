using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace V3SClient.Services
{
    internal sealed class SynchronizationTrackingHandler_v3 : DelegatingHandler
    {
        public SynchronizationTrackingHandler_v3(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using (AppSynchronizationStatus_v3.Begin())
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
