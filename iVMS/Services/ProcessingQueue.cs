using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;

namespace V3SClient.Services
{
    /// <summary>
    /// Dữ liệu nhận dạng từ JSON của hệ thống chụp ảnh biển số
    /// </summary>
    public class PlateRecognitionData
    {
        public string ImagePath { get; set; }        // FRONT_xxx.jpg
        public string JsonPath { get; set; }         // FRONT_xxx.json
        public string PlateImagePath { get; set; }   // FRONT_xxx_plate.jpg
        public string Plate { get; set; }            // "29B41541"
        public double Confidence { get; set; }       // 0.999...
        public string CaptureRole { get; set; }      // "front" / "back"
        public string CaptureTime { get; set; }      // snapshot_at
        public string CamId { get; set; }            // capture_cam_id
    }

    public class ProcessingTask
    {
        public long JobId { get; set; }
        public string FilePath { get; set; }
        public string FileType { get; set; } // "LicensePlate", "Document", etc.
        public int RetryCount { get; set; } = 0;

        // ── Hỗ trợ Mode B (thư mục biển số từ hệ thống chụp ảnh) ──
        /// <summary>True nếu task này là xử lý cả thư mục biển số (Mode B)</summary>
        public bool IsFolderTask { get; set; } = false;
        /// <summary>Đường dẫn thư mục biển số (khi IsFolderTask = true)</summary>
        public string FolderPath { get; set; }
        /// <summary>Biển số đã nhận dạng từ JSON (pre-extracted)</summary>
        public string PreExtractedPlate { get; set; }
        /// <summary>Confidence cao nhất từ các JSON trong folder</summary>
        public double PlateConfidence { get; set; }
        /// <summary>Danh sách metadata từ JSON cho từng ảnh</summary>
        public List<PlateRecognitionData> RecognitionDataList { get; set; }
    }

    public class ProcessingQueue
    {
        private readonly BlockingCollection<ProcessingTask> _queue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private int _pendingCount = 0;

        public int PendingCount => _pendingCount;

        public ProcessingQueue()
        {
            _queue = new BlockingCollection<ProcessingTask>(new ConcurrentQueue<ProcessingTask>());
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Enqueue(ProcessingTask task)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add(task);
                Interlocked.Increment(ref _pendingCount);
                LoggerManager.LogDebug($"Đã đưa file vào hàng đợi: {task.FilePath}");
            }
        }

        public ProcessingTask Dequeue(CancellationToken cancellationToken)
        {
            try
            {
                var task = _queue.Take(cancellationToken);
                Interlocked.Decrement(ref _pendingCount);
                return task;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }
    }
}
