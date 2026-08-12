using System;
using System.Threading;

namespace V3SClient.Services
{
    /// <summary>
    /// Reference-counted activity state shared by API callers and the shell.
    /// It intentionally has no WPF dependency, so networking stays usable
    /// before a window is shown.
    /// </summary>
    public static class AppSynchronizationStatus_v3
    {
        private static int _pendingOperations;

        public static event EventHandler SynchronizationChanged;

        public static bool IsSynchronizing
        {
            get { return Volatile.Read(ref _pendingOperations) > 0; }
        }

        public static IDisposable Begin()
        {
            if (Interlocked.Increment(ref _pendingOperations) == 1)
                RaiseSynchronizationChanged();
            return new Scope();
        }

        private static void End()
        {
            var remaining = Interlocked.Decrement(ref _pendingOperations);
            if (remaining <= 0)
            {
                if (remaining < 0)
                    Interlocked.Exchange(ref _pendingOperations, 0);
                RaiseSynchronizationChanged();
            }
        }

        private static void RaiseSynchronizationChanged()
        {
            var handler = SynchronizationChanged;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        private sealed class Scope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    End();
            }
        }
    }
}
