using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Documents.DocumentStructures;
using System.Windows.Markup;
using System.Windows.Media;
using Gst.Video;
using V3SClient.models;
using V3SClient.UI.Pages;

namespace V3SClient.libs
{
    public class VideoFiles
    {

        public static Tuple<Dictionary<string,List<string>>, List<Run> >FindVideoFiles(
            DateTime fromDateTime, DateTime toDateTime,List<Camera>camList, VideoStorage root,string searchExtension)
        {
            LoggerManager.LogDebug($"Bắt đầu tìm kiếm file video từ {fromDateTime} đến {toDateTime} tại {root?.Location}");
            int totalDays = ((TimeSpan)(toDateTime - fromDateTime)).Days;
            string extension = searchExtension.Replace("*", "");
            string fromSearch = ConvertDateTime2String(fromDateTime) + extension;
            string toSearch = ConvertDateTime2String(toDateTime) + extension;
           
            Dictionary<string, List<string>>camWithFiles = new Dictionary<string, List<string>>();
            foreach(Camera cam in camList)
                camWithFiles[cam.camID] = new List<string>();

            // Video lưu theo cấu trúc root/Year/month/Day/CamID/*.mp4
            GlobalClass.RemoveAllMp4Tmp();
            for (int i = 0; i <= totalDays; ++i)// Duyệt từng ngày
            {
                DateTime dateTime = fromDateTime.AddDays(i);
                string datePath = Path.Combine(
                    dateTime.Year.ToString("D4"),
                    dateTime.Month.ToString("D2"),
                    dateTime.Day.ToString("D2")
                );

                string baseDateFolder = Path.Combine(root.Location, datePath);

                foreach (Camera cam in camList) // duyệt từng camera
                {
                    string camFolder = Path.Combine(baseDateFolder, cam.camID);
                    if (!Directory.Exists(camFolder)) 
                    {
                        LoggerManager.LogDebug($"Thư mục không tồn tại: {camFolder}");
                        continue;
                    }

                    string[] videoFiles = Directory.GetFiles(camFolder, searchExtension);
                    Array.Sort(videoFiles);
                    var validFiles = videoFiles
                        .Where(file => IsFileWithinRange(file, fromSearch, toSearch))
                        .Where(file => new FileInfo(file).Length > 0) // bỏ qua file 0 byte
                        .ToList();

                    if (validFiles.Count == 0) continue;

                    LoggerManager.LogInfo($"Tìm thấy {validFiles.Count} file {extension} cho Camera: {cam.name} ({cam.camID}) tại {camFolder}");

                    if (extension == ".ts")
                    {
                        string[] mp4Files = validFiles.Select(file => file.Replace(".ts", ".mp4")).ToArray();

                        // Chuyển đổi file ts sang mp4
                        LoggerManager.FileProcessingLog(NLog.LogLevel.Info, $"Bắt đầu chuyển đổi {validFiles.Count} file .ts sang .mp4 cho camera {cam.camID}");
                        MediaHelper.ConvertToMp4(validFiles,mp4Files.ToList());

                        // Lọc bỏ các file mp4 lỗi hoặc 0 byte
                        List<string> validMp4Files = mp4Files
                        .Where(mp4 => File.Exists(mp4) && new FileInfo(mp4).Length > 0 && MediaHelper.IsMp4FileValidUseCv(mp4))
                        .ToList();

                        if (validMp4Files.Count < mp4Files.Length)
                        {
                            LoggerManager.LogWarn($"Có {mp4Files.Length - validMp4Files.Count} file mp4 không hợp lệ sau chuyển đổi cho camera {cam.camID}");
                        }

                        camWithFiles[cam.camID].AddRange(validMp4Files);
                        GlobalClass.UpdateOrAddKeyPairMp4Tmp(cam.camID, validMp4Files.ToList());
                    }
                    else if (extension == ".mp4")
                    {
                        camWithFiles[cam.camID].AddRange(validFiles);
                    }
                }
            }

            // Xác định nội dung message
            // Log UI feedback
            List<Run> logTextBlock = camList
                .Select(cam => CreateStatusRun(cam.name, camWithFiles[cam.camID].Count))
                .OrderBy(run => ((SolidColorBrush)run.Foreground).Color.ToString())
                .ToList();

            // Remove cameras with no videos
            var filteredCamWithFiles = camWithFiles
                .Where(pair => pair.Value.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            return Tuple.Create(filteredCamWithFiles, logTextBlock);

        }
        private static bool IsFileWithinRange(string filePath, string fromSearch, string toSearch)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 1) return false;

            string fileName = Path.GetFileName(filePath);
            //string timePart = string.Join("_", fileName.Split('_').Reverse().Take(6).Reverse());
            string[] parts = fileName.Split('_');
            if (parts.Length < 7) return false;
            string timePart = string.Join("_", parts.Skip(1).Take(6));
            return string.Compare(timePart, fromSearch, StringComparison.Ordinal) >= 0
                && string.Compare(timePart, toSearch, StringComparison.Ordinal) <= 0;
        }

        private static Run CreateStatusRun(string camName, int count)
        {
            var run = new Run(count == 0
                ? $"{camName} --> None \n"
                : $"{camName} --> OK ({count}) \n");

            run.Foreground = new SolidColorBrush(
                count == 0 ? Colors.OrangeRed : Colors.WhiteSmoke
            );
            return run;
        }
        public static string ConvertDateTime2String(DateTime dateTime)
        {
            // yyyy-MM-dd-hh-mm-ss
            string str = string.Format("{0:D2}_{1:D2}_{2:D2}_{3:D2}_{4:D2}_{5:D2}",
                dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second);
            return str;
        }
    }
}
