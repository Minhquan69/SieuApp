using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaToolkit.Model;
using MediaToolkit;
using System.Diagnostics;
using System.IO;
using System.Threading;
using V3SClient.enums;

namespace V3SClient.libs
{
    public class MediaHelper
    {
        private static string ffprobePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffprobe.exe");
        private static string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");

        public static bool ConvertToMp4(List<string> inputPaths, List<string> outputPaths, int maxParallel = 4)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(maxParallel);
            List<Task> allTasks = new List<Task>();

            for (int idx = 0; idx < inputPaths.Count; idx++)
            {
                int currentIdx = idx;

                Task task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();

                    try
                    {
                        if (File.Exists(outputPaths[currentIdx])) return;
                        FileInfo fileCheck = new FileInfo(inputPaths[currentIdx]);
                        if (fileCheck.Length == 0) return;
                        if (!File.Exists(ffmpegPath)) return ;
                        string args = $"-probesize 10M -analyzeduration 20M -i \"{inputPaths[currentIdx]}\" -c:v copy -c:a copy \"{outputPaths[currentIdx]}\"";

                        using (Process process = new Process())
                        {
                            process.StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = args,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,//Quan tro?ng cho FFMPEG
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };

                            process.Start();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit();
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, $"Lỗi khi chuyển đổi file bằng FFmpeg: {inputPaths[currentIdx]}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                allTasks.Add(task);
            }

            Task.WaitAll(allTasks.ToArray());
            return true;
        }
        public static TimeSpan GetVideoDuration(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.WriteLine("File path cannot be null or empty.");
                return TimeSpan.Zero; 
            }
            var inputFile = new MediaFile { Filename = filePath };
            using (var engine = new Engine())
            {
                try
                {
                    engine.GetMetadata(inputFile);
                    if (inputFile.Metadata == null)
                    {
                        LoggerManager.LogWarn($"Không thể trích xuất metadata từ file video: {filePath}");
                        return TimeSpan.Zero; 
                    }
                    if (inputFile.Metadata.Duration == null || inputFile.Metadata.Duration.TotalSeconds <= 0)
                    {
                        LoggerManager.LogWarn($"Độ dài video không hợp lệ hoặc không thể trích xuất từ file: {filePath}");
                        return TimeSpan.Zero; 
                    }
                    return inputFile.Metadata.Duration; 
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"Lỗi khi đọc độ dài video file {filePath}");
                    return TimeSpan.Zero; 
                }
            }
        }
        public static bool IsMp4FileValidUseFfprobe(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            if (!File.Exists(ffprobePath)) return false;
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v error -show_format -show_streams \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // N?u có output ho?c không có l?i thì file h?p l?
                return !string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(error);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi kiểm tra tính hợp lệ file video bằng ffprobe: {filePath}");
                return false;
            }
        }
        public static bool IsMp4FileValidUseCv(string filePath)
        {
            try
            {
                using (var cap = new OpenCvSharp.VideoCapture(filePath))
                {
                    if (!cap.IsOpened()) return false;

                    double width = cap.Get(OpenCvSharp.VideoCaptureProperties.FrameWidth);
                    double height = cap.Get(OpenCvSharp.VideoCaptureProperties.FrameHeight);
                    double fps = cap.Get(OpenCvSharp.VideoCaptureProperties.Fps);
                    double frameCount = cap.Get(OpenCvSharp.VideoCaptureProperties.FrameCount);

                    return width > 0 && height > 0 && fps > 0 && frameCount > 0;
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi kiểm tra tính hợp lệ file video bằng OpenCV: {filePath}");
                return false;
            }

        }

        public static MediaType GetMediaType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            string[] videoExts = { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".flv", ".webm" };
            string[] imageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
            string[] audioExts = { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a" };

            if (videoExts.Contains(ext))
                return MediaType.Video;
            else if (imageExts.Contains(ext))
                return MediaType.Image;
            else if (audioExts.Contains(ext))
                return MediaType.Audio;
            else
                return MediaType.Unknow;
        }
    }
}















