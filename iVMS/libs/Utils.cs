
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using V3SClient.UI.Converters;
using V3SClient.enums;

namespace V3SClient.libs
{
    public class Utils
    {

        public static void CloseAndResetApp()
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeName = System.IO.Path.GetFileName(exePath);
            string batPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "restart_app.bat");

            string batContent = $@"
                                @echo off
                                :waitloop
                                tasklist | findstr /i ""{exeName}"" >nul
                                if not errorlevel 1 (
                                    timeout /t 1 >nul
                                    goto waitloop
                                )
                                start """" ""{exePath}""
                                del ""%~f0""
                                exit
                                ";

            System.IO.File.WriteAllText(batPath, batContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Application.Current.Shutdown();
        }

        public static List<AreaNode> BuildTree(List<CamInfo> camList)
        {
            var result = new List<AreaNode>();
            var comparer = new NaturalStringComparer();

            foreach (var cam in camList)
            {
                // If we have a region path, build recursively
                if (cam.Region_Path != null && cam.Region_Path.Count > 0)
                {
                    // 1. Handle Root (Area)
                    var rootInfo = cam.Region_Path[0];
                    var area = result.FirstOrDefault(p => p.Name == rootInfo.Name);
                    if (area == null)
                    {
                        area = new AreaNode { Name = rootInfo.Name };
                        result.Add(area);
                    }

                    // 2. Handle Intermediate levels recursively
                    UnitNode currentContainer = null;
                    for (int i = 1; i < cam.Region_Path.Count; i++)
                    {
                        var levelInfo = cam.Region_Path[i];
                        if (i == 1)
                        {
                            currentContainer = area.Units.FirstOrDefault(b => b.Name == levelInfo.Name);
                            if (currentContainer == null)
                            {
                                currentContainer = new UnitNode { Name = levelInfo.Name };
                                area.Units.Add(currentContainer);
                            }
                        }
                        else
                        {
                            var subContainer = currentContainer.SubUnits.FirstOrDefault(b => b.Name == levelInfo.Name);
                            if (subContainer == null)
                            {
                                subContainer = new UnitNode { Name = levelInfo.Name };
                                currentContainer.SubUnits.Add(subContainer);
                            }
                            currentContainer = subContainer;
                        }
                    }

                    // 3. Add camera to the leaf container
                    if (cam.Device_Role != "central_radio" ) // Allow radio commanders in tree if body_cam
                    {
                        var targetNode = new CamInfoNode { CamData = cam };
                        if (currentContainer != null)
                        {
                            currentContainer.Cams.Add(targetNode);
                        }
                        else
                        {
                            // If only 1 level in path, add to a dummy unit or handle differently
                            var defaultUnit = area.Units.FirstOrDefault(b => b.Name == "None");
                            if (defaultUnit == null)
                            {
                                defaultUnit = new UnitNode { Name = "None" };
                                area.Units.Add(defaultUnit);
                            }
                            defaultUnit.Cams.Add(targetNode);
                        }
                    }
                }
                else
                {
                    if (cam.Device_Role == "central_radio") continue;

                    // Legacy fallback
                    var areaName = string.IsNullOrEmpty(cam.Area_Name) || cam.Area_Name == "None" ? "None" : cam.Area_Name;
                    var area = result.FirstOrDefault(p => p.Name == areaName);
                    if (area == null)
                    {
                        area = new AreaNode { Name = areaName };
                        result.Add(area);
                    }

                    var unitName = string.IsNullOrEmpty(cam.Unit_Name) || cam.Unit_Name == "None" ? "None" : cam.Unit_Name;
                    var unit = area.Units.FirstOrDefault(b => b.Name == unitName);
                    if (unit == null)
                    {
                        unit = new UnitNode { Name = unitName };
                        area.Units.Add(unit);
                    }

                    unit.Cams.Add(new CamInfoNode { CamData = cam });
                }
            }

            // Recursive sorting
            foreach (var area in result)
            {
                SortUnitRecursive(area.Units, comparer);
                area.NotifyItemsChanged(); // Ensure UI refresh for root units
            }

            return result.OrderBy(a => a.Name, comparer).ToList();
        }

        private static void SortUnitRecursive(ObservableCollection<UnitNode> units, NaturalStringComparer comparer)
        {
            if (units == null || units.Count == 0) return;

            // Sort current level units
            var sortedUnits = units.OrderBy(u => u.Name, comparer).ToList();
            units.Clear();
            foreach (var u in sortedUnits)
            {
                // Sort cameras in this unit
                if (u.Cams != null && u.Cams.Count > 1)
                {
                    var sortedCams = u.Cams.OrderBy(c => c.CamData.CamInfo_Name, comparer).ToList();
                    u.Cams.Clear();
                    foreach (var c in sortedCams) u.Cams.Add(c);
                }

                // Recurse sub-units
                SortUnitRecursive(u.SubUnits, comparer);
                
                u.NotifyItemsChanged(); // Trigger UI refresh for 'Items'
                units.Add(u);
            }
        }

        public static string GetVietnameseNumberFromCameraName(string cameraName)
        {
            if (string.IsNullOrWhiteSpace(cameraName)) return "không rõ";

            // Clean name for better TTS reading
            string cleanName = cameraName.Replace("Cam", "").Trim();

            // Try to find a numeric sequence
            Match match = Regex.Match(cleanName, @"\d+");
            if (match.Success)
            {
                if (int.TryParse(match.Value, out int number))
                {
                    if (number >= 1 && number <= 1000)
                    {
                        return ConvertToVietnameseNumber(number);
                    }
                }
            }

            // Fallback: Use the original name (minus "Cam") for text-based cameras
            return cleanName;
        }

        public static string ConvertToVietnameseNumber(int number)
        {
            if (number < 1 || number > 1000)
                return "không hỗ trợ";

            string[] donvi = { "", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
            string result = "";

            if (number == 1000)
                return "một nghìn";

            int hundreds = number / 100;
            int tens = (number % 100) / 10;
            int units = number % 10;

            if (hundreds > 0)
            {
                result += donvi[hundreds] + " trăm";

                if (tens == 0 && units > 0)
                    result += " linh";
            }

            if (tens > 0)
            {
                if (result != "") result += " ";
                if (tens == 1)
                    result += "mười";
                else
                    result += donvi[tens] + " mươi";
            }

            if (units > 0)
            {
                if (result != "") result += " ";

                if (tens == 0 && hundreds == 0)
                    result += donvi[units];
                else if (units == 1)
                    result += (tens > 1) ? "mốt" : "một";
                else if (units == 5 && tens >= 1)
                    result += "lăm";
                else
                    result += donvi[units];
            }

            return result.Trim();
        }
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        public static ItemEditField CreateField(string label, string bindingPath, EditControlType type, object editItem, List<ComboBoxItemModel> comboItems = null)
        {
            return new ItemEditField
            {
                Label = label,
                BindingPath = bindingPath,
                ControlType = type,
                ComboItems = comboItems,
                Converter = new ReflectionBindingConverter(editItem)
            };
        }
        public static string GetFfmpegPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Không tìm thấy ffmpeg.exe", path);
                return path;
            }

            return "ffmpeg"; // Linux/Mac: assume in PATH
        }
        public static bool IsRtspUrlValid(string url, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                error = "❌ Đường dẫn RTSP trống.";
                return false;
            }
            string ffmpegPath = GetFfmpegPath();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-rtsp_transport tcp -i \"{url}\" -t 1 -f null - -v error",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    StringBuilder stderrBuilder = new StringBuilder();
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                            stderrBuilder.AppendLine(args.Data);
                    };

                    process.Start();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch { }
                        error = "⌛ RTSP timeout (không phản hồi trong 3 giây).";
                        return false;
                    }

                    process.WaitForExit(); // đảm bảo flush hết stderr

                    string stderr = stderrBuilder.ToString();
                    if (process.ExitCode != 0 || stderr.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.WriteLine($"❌ FFmpeg Lỗi: {stderr.Trim()}");
                        error = "❌ Link Rtsp không thể kết nối.";
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ FFmpeg Lỗi: {ex}");
                error = "❗ RTSP không thể kết nối. Phát sinh ngoại lệ!";
                return false;
            }
        }

    }
}
