using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Gst;
using Task = System.Threading.Tasks.Task;

namespace V3SClient.models
{
    /// <summary>
    /// Continuously removes messages from a GstBus. SyncMessage handlers only
    /// observe messages before they are queued; this pump owns and disposes the
    /// messages returned by TimedPop so the native queue cannot grow forever.
    /// </summary>
    internal sealed class GstBusMessagePump : IDisposable
    {
        private readonly Bus _bus;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Task _pumpTask;
        private int _disposed;

        public GstBusMessagePump(Bus bus)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));

            _bus = bus;
            _pumpTask = Task.Factory.StartNew(
                DrainMessages,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public Bus Bus
        {
            get { return _bus; }
        }

        private void DrainMessages()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                Gst.Message message = null;
                try
                {
                    // This is a blocking native wait, not a busy loop. A posted
                    // message wakes it immediately; the timeout only bounds
                    // shutdown latency when the bus is idle.
                    message = _bus.TimedPop(100 * Gst.Constants.MSECOND);
                }
                catch (Exception ex)
                {
                    if (!_cancellation.IsCancellationRequested)
                        Trace.TraceError("GStreamer bus message pump stopped unexpectedly: {0}", ex);
                    return;
                }
                finally
                {
                    // TimedPop transfers ownership of the message to us.
                    message?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cancellation.Cancel();
            try
            {
                // TimedPop has a 100 ms timeout, so this wait is bounded during
                // normal operation and guarantees the bus is no longer in use
                // before its managed/native reference is released.
                _pumpTask.Wait();
            }
            catch (AggregateException ex)
            {
                Trace.TraceError("Could not stop the GStreamer bus message pump cleanly: {0}", ex.Flatten());
            }
            finally
            {
                _cancellation.Dispose();
                _bus.Dispose();
            }
        }
    }
}
