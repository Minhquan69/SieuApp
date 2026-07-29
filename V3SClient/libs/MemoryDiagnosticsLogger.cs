using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace V3SClient.libs
{
    /// <summary>
    /// Lightweight, per-run memory telemetry.  It observes the process only;
    /// it never forces a collection or otherwise changes playback behaviour.
    /// </summary>
    internal sealed class MemoryDiagnosticsLogger : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private readonly StreamWriter _writer;
        private bool _disposed;

        public string FilePath { get; private set; }

        public MemoryDiagnosticsLogger(TimeSpan sampleInterval)
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            Directory.CreateDirectory(downloads);

            FilePath = Path.Combine(
                downloads,
                "iVista-VMS-memory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
            _writer.WriteLine("timestamp,event,managed_heap_mb,working_set_mb,private_bytes_mb,paged_memory_mb,handles,threads,gc_gen0,gc_gen1,gc_gen2");
            _writer.Flush();

            WriteSample("startup");
            _timer = new Timer(_ => WriteSample("interval"), null, sampleInterval, sampleInterval);
        }

        public void WriteSample(string eventName)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                try
                {
                    using (var process = Process.GetCurrentProcess())
                    {
                        process.Refresh();
                        _writer.WriteLine(string.Join(",",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            eventName,
                            ToMb(GC.GetTotalMemory(false)),
                            ToMb(process.WorkingSet64),
                            ToMb(process.PrivateMemorySize64),
                            ToMb(process.PagedMemorySize64),
                            process.HandleCount.ToString(CultureInfo.InvariantCulture),
                            process.Threads.Count.ToString(CultureInfo.InvariantCulture),
                            GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture),
                            GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture),
                            GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture)));
                        _writer.Flush();
                    }
                }
                catch
                {
                    // Diagnostics must never destabilize the VMS process.
                }
            }
        }

        private static string ToMb(long bytes)
        {
            return (bytes / 1024d / 1024d).ToString("F2", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                WriteSample("shutdown");
                _disposed = true;
                _timer.Dispose();
                _writer.Dispose();
            }
        }
    }
}
