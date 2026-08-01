using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Gst;
using Task = System.Threading.Tasks.Task;

namespace V3SClient.models
{
    /// <summary>
    /// Drains queued native bus messages. SyncMessage observes a message but
    /// does not remove it from GstBus; without a consumer the native queue can
    /// grow for every long-running camera tile.
    /// </summary>
    internal sealed class GstBusMessagePump : IDisposable
    {
        private readonly Bus _bus;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Task _pumpTask;
        private int _disposed;

        public GstBusMessagePump(Bus bus)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _bus = bus;
            _pumpTask = Task.Factory.StartNew(DrainMessages, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public Bus Bus { get { return _bus; } }

        private void DrainMessages()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                Gst.Message message = null;
                try
                {
                    // Blocking wait, bounded so release never waits on an idle bus.
                    message = _bus.TimedPop(100 * Gst.Constants.MSECOND);
                }
                catch (Exception ex)
                {
                    if (!_cancellation.IsCancellationRequested)
                        Trace.TraceError("GStreamer bus pump stopped unexpectedly: {0}", ex);
                    return;
                }
                finally
                {
                    // TimedPop transfers the native message reference to us.
                    if (message != null) message.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cancellation.Cancel();
            try { _pumpTask.Wait(); }
            catch (AggregateException ex) { Trace.TraceError("GStreamer bus pump shutdown failed: {0}", ex.Flatten()); }
            finally
            {
                _cancellation.Dispose();
                _bus.Dispose();
            }
        }
    }
}
