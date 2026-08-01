using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class FileHelper
    {
        public static DateTime? ExtractTimestampFromFileName(string fileName)
        {
            // Lấy tên file không có đuôi mở rộng
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            // Tìm chuỗi thời gian theo đúng định dạng yyyy_MM_dd_HH_mm_ss
            var match = Regex.Match(baseName, @"\d{4}_\d{2}_\d{2}_\d{2}_\d{2}_\d{2}$");

            if (match.Success)
            {
                string timestampString = match.Value;

                if (DateTime.TryParseExact(timestampString, "yyyy_MM_dd_HH_mm_ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
                {
                    return timestamp;
                }
            }

            return null;
        }

        /// <summary>
        /// Trả về tất cả các file trong thư mục theo định dạng được chỉ định
        /// </summary>
        public static ConcurrentDictionary<string, FileInfo> LoadFilesToCache(string rootDirectory, List<string> allowedExtensions)
        {
            var result = new ConcurrentDictionary<string, FileInfo>();

            if (!Directory.Exists(rootDirectory))
            {
                Debug.WriteLine($"📁 Thư mục không tồn tại: {rootDirectory}");
                return result;
            }

            try
            {
                var lowerExtensions = new HashSet<string>();
                if (allowedExtensions != null)
                {
                    foreach (var ext in allowedExtensions)
                        lowerExtensions.Add(ext.ToLower());
                }

                var allFiles = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories);

                Parallel.ForEach(allFiles, filePath =>
                {
                    try
                    {
                        var ext = Path.GetExtension(filePath).ToLower();
                        if (lowerExtensions.Count == 0 || lowerExtensions.Contains(ext))
                        {
                            var fileInfo = new FileInfo(filePath);
                            result.TryAdd(fileInfo.Name, fileInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ Không thể lấy FileInfo cho {filePath}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Lỗi khi duyệt thư mục: {ex.Message}");
            }

            return result;
        }
    }
}
