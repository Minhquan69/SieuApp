using System;
using System.IO;
using System.Security.Cryptography;
using V3SClient.libs;
using VehicleDocumentProcessing.WPF.Models;

namespace V3SClient.Services
{
    public class FileNamingService
    {
        private readonly NamingRule _plateNamingRule;
        private readonly NamingRule _documentNamingRule;

        public FileNamingService(NamingRule plateRule, NamingRule documentRule)
        {
            _plateNamingRule = plateRule;
            _documentNamingRule = documentRule;
        }

        /// <summary>
        /// Đổi tên và di chuyển file vào thư mục output theo biển số.
        /// outputDirectory được truyền từ pipeline để hỗ trợ 2 output dirs riêng biệt.
        /// </summary>
        public string ProcessAndMoveFile(string originalFilePath, string documentType, string licensePlate, string outputDirectory, bool isVehiclePhoto, bool isCopyMode = false, System.Collections.Generic.Dictionary<string, string> extraFields = null)
        {
            try
            {
                string safePlate = GetProcessedLicensePlate(licensePlate, isVehiclePhoto);
                
                // Tạo thư mục đích theo biển số xe
                string targetDirectory = Path.Combine(outputDirectory, safePlate);

                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                string extension = Path.GetExtension(originalFilePath);
                string newFileName = GenerateFileName(documentType, licensePlate, extraFields, extension, isVehiclePhoto);
                
                string targetFilePath = Path.Combine(targetDirectory, newFileName);

                // Tránh ghi đè file trùng tên
                if (File.Exists(targetFilePath))
                {
                    if (File.Exists(originalFilePath) && FilesHaveSameContent(originalFilePath, targetFilePath))
                        return targetFilePath;

                    string fileNameNoExt = Path.GetFileNameWithoutExtension(targetFilePath);
                    long sourceTicks = File.Exists(originalFilePath) ? File.GetLastWriteTimeUtc(originalFilePath).Ticks : 0;
                    targetFilePath = Path.Combine(targetDirectory, $"{fileNameNoExt}_{sourceTicks}{extension}");
                    if (File.Exists(targetFilePath))
                    {
                        if (File.Exists(originalFilePath) && FilesHaveSameContent(originalFilePath, targetFilePath))
                            return targetFilePath;
                        return null;
                    }
                }

                // Di chuyển hoặc copy file
                if (File.Exists(originalFilePath))
                {
                    if (isCopyMode)
                    {
                        File.Copy(originalFilePath, targetFilePath, true);
                        LoggerManager.LogInfo($"Đã copy file thành công: {targetFilePath}");
                    }
                    else
                    {
                        File.Move(originalFilePath, targetFilePath);
                        LoggerManager.LogInfo($"Đã di chuyển file thành công: {targetFilePath}");
                    }
                    return targetFilePath;
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi đổi tên và di chuyển file: {originalFilePath}", ex);
            }
            return null;
        }

        private static bool FilesHaveSameContent(string firstPath, string secondPath)
        {
            var firstInfo = new FileInfo(firstPath);
            var secondInfo = new FileInfo(secondPath);
            if (firstInfo.Length != secondInfo.Length) return false;

            using (var algorithm = SHA256.Create())
            using (var firstStream = File.OpenRead(firstPath))
            using (var secondStream = File.OpenRead(secondPath))
            {
                byte[] firstHash = algorithm.ComputeHash(firstStream);
                byte[] secondHash = algorithm.ComputeHash(secondStream);
                for (int index = 0; index < firstHash.Length; index++)
                {
                    if (firstHash[index] != secondHash[index]) return false;
                }
                return true;
            }
        }

        private string GenerateFileName(string docType, string licensePlate, System.Collections.Generic.Dictionary<string, string> extraFields, string extension, bool isVehiclePhoto)
        {
            var rule = isVehiclePhoto ? _plateNamingRule : _documentNamingRule;
            if (rule == null || rule.Segments.Count == 0)
                return $"Unknown_{DateTime.Now:HHmmss}{extension}";

            var parts = new System.Collections.Generic.List<string>();

            foreach (var segment in rule.Segments)
            {
                if (segment.SegmentType == "Text")
                {
                    parts.Add(MakeValidFileName(segment.Value));
                }
                else if (segment.SegmentType == "Field")
                {
                    string fieldValue = GetFieldValue(segment.Value, docType, licensePlate, extraFields);
                    
                    // Thực hiện replace ký tự nếu có cấu hình riêng cho segment này
                    foreach (var replacement in segment.ReplacementRules)
                    {
                        if (!string.IsNullOrEmpty(replacement.SourceChars))
                        {
                            foreach (char c in replacement.SourceChars)
                            {
                                fieldValue = fieldValue.Replace(c.ToString(), replacement.TargetChar ?? "");
                            }
                        }
                    }

                    parts.Add(MakeValidFileName(fieldValue));
                }
            }

            string name = string.Join(rule.Separator ?? "_", parts);
            
            // Xóa các ký tự phân tách dư thừa nếu có trường bị trống
            name = name.Replace($"{rule.Separator}{rule.Separator}", rule.Separator).Trim((rule.Separator ?? "_").ToCharArray());

            return name + extension;
        }

        private string GetFieldValue(string key, string docType, string licensePlate, System.Collections.Generic.Dictionary<string, string> extraFields)
        {
            if (key == "bien_so") return string.IsNullOrWhiteSpace(licensePlate) ? "Unknown" : licensePlate;
            if (key == "loai_giay_to") return docType;
            if (key == "{timestamp}")
            {
                if (extraFields != null && extraFields.TryGetValue("source_timestamp", out string sourceTimestamp) &&
                    !string.IsNullOrWhiteSpace(sourceTimestamp))
                    return sourceTimestamp;
                return DateTime.Now.ToString("HHmmss");
            }
            if (key == "{date}") return DateTime.Now.ToString("yyyyMMdd");
            if (key == "{guid}") return Guid.NewGuid().ToString().Substring(0, 6);
            if (key == "mau_bien_so")
            {
                if (extraFields != null && extraFields.TryGetValue("mau_bien_so", out string mauBienSo))
                {
                    if (mauBienSo.IndexOf("Xanh", StringComparison.OrdinalIgnoreCase) >= 0) return "X";
                    if (mauBienSo.IndexOf("Vàng", StringComparison.OrdinalIgnoreCase) >= 0) return "V";
                    if (mauBienSo.IndexOf("Trắng", StringComparison.OrdinalIgnoreCase) >= 0) return "T";
                    if (mauBienSo.IndexOf("Đỏ", StringComparison.OrdinalIgnoreCase) >= 0) return "D";
                    return mauBienSo;
                }
                return "";
            }

            if (extraFields != null && extraFields.TryGetValue(key, out string val))
            {
                return val;
            }
            return "";
        }

        public string GetProcessedLicensePlate(string rawPlate, bool isVehiclePhoto)
        {
            if (string.IsNullOrWhiteSpace(rawPlate)) return "UnknownPlate";
            
            var rule = isVehiclePhoto ? _plateNamingRule : _documentNamingRule;
            if (rule == null || rule.Segments == null) return MakeValidFileName(rawPlate);

            string processedPlate = rawPlate;
            foreach (var segment in rule.Segments)
            {
                if (segment.SegmentType == "Field" && segment.Value == "bien_so" && segment.ReplacementRules != null)
                {
                    foreach (var replacement in segment.ReplacementRules)
                    {
                        if (!string.IsNullOrEmpty(replacement.SourceChars))
                        {
                            foreach (char c in replacement.SourceChars)
                            {
                                processedPlate = processedPlate.Replace(c.ToString(), replacement.TargetChar ?? "");
                            }
                        }
                    }
                    break;
                }
            }
            return MakeValidFileName(processedPlate);
        }

        private string MakeValidFileName(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "Unknown";
                
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
            {
                text = text.Replace(c.ToString(), "");
            }
            return text.Replace(" ", "_");
        }
    }
}
