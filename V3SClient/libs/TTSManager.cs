using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public sealed class TTSManager : IDisposable
    {
        private static readonly Lazy<TTSManager> _instance = new Lazy<TTSManager>(() => new TTSManager());
        public static TTSManager Instance => _instance.Value;

        private readonly BlockingCollection<string> _speakQueue = new BlockingCollection<string>();
        private readonly SemaphoreSlim _concurrentLimiter;
        private readonly Thread _workerThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly ConcurrentDictionary<string, DateTime> _cameraCooldowns = new ConcurrentDictionary<string, DateTime>();
        private readonly HashSet<string> _pendingCameras = new HashSet<string>();
        private readonly System.Timers.Timer _batchTimer;
        private readonly object _batchLock = new object();

        private readonly int _maxParallelSpeaks;
        private bool _disposed;

        private TTSManager(int maxParallel = 1)
        {
            _maxParallelSpeaks = maxParallel;
            _concurrentLimiter = new SemaphoreSlim(maxParallel);

            _batchTimer = new System.Timers.Timer(1500); // 1.5s accumulation window
            _batchTimer.AutoReset = false;
            _batchTimer.Elapsed += BatchTimer_Elapsed;

            _workerThread = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = "TTSWorker"
            };
            _workerThread.Start();
        }

        /// <summary>
        /// Batches camera warnings together to prevent overlapping audio
        /// </summary>
        public void EnqueueWarning(string cameraName)
        {
            if (_disposed || string.IsNullOrWhiteSpace(cameraName)) return;

            // 1. Check Cooldown (10s per camera)
            if (_cameraCooldowns.TryGetValue(cameraName, out DateTime lastTime))
            {
                if ((DateTime.Now - lastTime).TotalSeconds < 10)
                    return; // Skip if reported recently
            }

            _cameraCooldowns[cameraName] = DateTime.Now;

            // 2. Add to batch
            lock (_batchLock)
            {
                _pendingCameras.Add(cameraName);
                
                // Restart timer
                _batchTimer.Stop();
                _batchTimer.Start();
            }
        }

        private void BatchTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            List<string> camerasToAnnounce;
            lock (_batchLock)
            {
                if (_pendingCameras.Count == 0) return;
                camerasToAnnounce = _pendingCameras.ToList();
                _pendingCameras.Clear();
            }

            // Build sentence
            string message = "";
            if (camerasToAnnounce.Count == 1)
            {
                string name = Utils.GetVietnameseNumberFromCameraName(camerasToAnnounce[0]);
                message = $"Cảnh báo camera {name}";
            }
            else
            {
                var names = camerasToAnnounce.Select(c => Utils.GetVietnameseNumberFromCameraName(c)).ToList();
                string joinedNames = "";
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0)
                    {
                        if (i == names.Count - 1)
                            joinedNames += " và ";
                        else
                            joinedNames += ", ";
                    }
                    joinedNames += names[i];
                }
                message = $"Cảnh báo các camera {joinedNames}";
            }

            Enqueue(message);
        }

        /// <summary>
        /// Push text to speech
        /// </summary>
        public void Enqueue(string text)
        {
            if (!_disposed && !string.IsNullOrWhiteSpace(text))
                _speakQueue.Add(text);
        }
        /// <summary>
        /// Xóa toàn b? n?i dung trong hàng d?i d?c
        /// </summary>
        public void ClearQueue()
        {
            if (_disposed) return;

            // Rút toàn b? ph?n t? ra kh?i queue mà không x? lý
            while (_speakQueue.TryTake(out _, 0)) { }
        }
        /// <summary>
        /// Loop
        /// </summary>
        private void WorkLoop()
        {
            foreach (var text in _speakQueue.GetConsumingEnumerable(_cts.Token))
            {
                _concurrentLimiter.Wait(_cts.Token);

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (var synth = new SpeechSynthesizer())
                        {
                            synth.Rate = 3;
                            foreach (var voice in synth.GetInstalledVoices())
                            {
                                if (voice.VoiceInfo.Name.ToLower().Contains("thuy") || voice.VoiceInfo.Culture.Name.StartsWith("vi"))
                                {
                                    synth.SelectVoice(voice.VoiceInfo.Name);
                                    break;
                                }
                            }

                            synth.Speak(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("TTS Error: " + ex.Message);
                    }
                    finally
                    {
                        _concurrentLimiter.Release();
                    }
                });
            }
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts.Cancel();
                _speakQueue.CompleteAdding();
                _workerThread.Join();
                _speakQueue.Dispose();
                _concurrentLimiter.Dispose();
                _cts.Dispose();
            }
        }
    }
}















