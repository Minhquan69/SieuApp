using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace V3SClient.Services
{
    public class LatestFrameSnapshot
    {
        public byte[] JpegBytes { get; set; }
        public ImageSource PreviewImage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class RtspStreamService : IDisposable
    {
        private Process _previewProcess;
        private CancellationTokenSource _previewCts;
        private readonly object _syncRoot = new object();
        private byte[] _latestJpegFrame;
        private ImageSource _latestFrameImage;
        private DateTime _latestFrameTimestamp;

        public event Action<ImageSource> FrameReceived;
        public event Action<string> StatusChanged;

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                {
                    return _previewProcess != null && !_previewProcess.HasExited;
                }
            }
        }

        public static string ResolveBundledFfmpegPath()
        {
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
                throw new FileNotFoundException("Không tìm thấy ffmpeg.exe.", ffmpegPath);
            return ffmpegPath;
        }

        public void StartPreview(string ffmpegPath, string rtspUrl)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath))
                throw new ArgumentException("Đường dẫn FFmpeg đang trống.", nameof(ffmpegPath));
            if (string.IsNullOrWhiteSpace(rtspUrl))
                throw new ArgumentException("Link stream đang trống.", nameof(rtspUrl));

            StopPreview();

            _previewCts = new CancellationTokenSource();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = BuildPreviewArguments(rtspUrl),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    StatusChanged?.Invoke(args.Data);
            };
            process.Exited += (sender, args) =>
            {
                var cts = _previewCts;
                if (cts != null && !cts.IsCancellationRequested)
                    StatusChanged?.Invoke("Luồng stream đã dừng.");
            };

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("Không thể khởi động FFmpeg.");

                process.BeginErrorReadLine();
                lock (_syncRoot)
                {
                    _previewProcess = process;
                }

                StatusChanged?.Invoke("Luồng stream đang hoạt động.");
                Task.Run(() => ReadPreviewFrames(process, _previewCts.Token), _previewCts.Token);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        public void StopPreview()
        {
            Process processToStop = null;

            lock (_syncRoot)
            {
                _previewCts?.Cancel();
                processToStop = _previewProcess;
                _previewProcess = null;
            }

            if (processToStop != null)
            {
                try
                {
                    if (!processToStop.HasExited)
                        processToStop.Kill();
                }
                catch { }

                try
                {
                    processToStop.WaitForExit(1000);
                }
                catch { }

                processToStop.Dispose();
            }

            _previewCts?.Dispose();
            _previewCts = null;
            StatusChanged?.Invoke("Luồng RTSP đã dừng.");
        }

        public bool TryGetLatestFrameSnapshot(out LatestFrameSnapshot snapshot)
        {
            snapshot = null;

            lock (_syncRoot)
            {
                if (_latestJpegFrame == null || _latestJpegFrame.Length == 0 || _latestFrameImage == null)
                    return false;

                snapshot = new LatestFrameSnapshot
                {
                    JpegBytes = (byte[])_latestJpegFrame.Clone(),
                    PreviewImage = _latestFrameImage,
                    Timestamp = _latestFrameTimestamp
                };
            }

            return true;
        }

        private void ReadPreviewFrames(Process process, CancellationToken token)
        {
            var frameBuffer = new List<byte>(256 * 1024);
            var insideFrame = false;
            var previous = -1;
            var buffer = new byte[8192];

            try
            {
                var stream = process.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    for (var i = 0; i < read; i++)
                    {
                        var current = buffer[i];
                        if (!insideFrame)
                        {
                            if (previous == 0xFF && current == 0xD8)
                            {
                                frameBuffer.Clear();
                                frameBuffer.Add(0xFF);
                                frameBuffer.Add(0xD8);
                                insideFrame = true;
                            }
                        }
                        else
                        {
                            frameBuffer.Add(current);
                            if (previous == 0xFF && current == 0xD9)
                            {
                                PublishFrame(frameBuffer.ToArray());
                                frameBuffer.Clear();
                                insideFrame = false;
                            }
                        }

                        previous = current;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    StatusChanged?.Invoke("Không đọc được luồng stream: " + ex.Message);
            }
        }

        private void PublishFrame(byte[] jpegBytes)
        {
            try
            {
                using (var memoryStream = new MemoryStream(jpegBytes))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = memoryStream;
                    image.EndInit();
                    image.Freeze();

                    lock (_syncRoot)
                    {
                        _latestJpegFrame = (byte[])jpegBytes.Clone();
                        _latestFrameImage = image;
                        _latestFrameTimestamp = DateTime.Now;
                    }

                    FrameReceived?.Invoke(image);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Không hiển thị được khung hình: " + ex.Message);
            }
        }

        private static string BuildPreviewArguments(string rtspUrl)
        {
            return "-hide_banner -loglevel warning -rtsp_transport tcp -i " + Quote(rtspUrl) + " -an -vf fps=10 -f image2pipe -vcodec mjpeg -q:v 5 pipe:1";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public void Dispose()
        {
            StopPreview();
        }
    }
}
