using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace V3SClient.libs
{
    public static class LoggerManager
    {
        static LoggerManager()
        {
            // Tải cấu hình từ file NLog.config
            LogManager.LoadConfiguration("NLog.config");
        }

        private static readonly Logger generalLogger = LogManager.GetLogger("GeneralLogger");
        private static readonly Logger deviceLogger = LogManager.GetLogger("DeviceLogger");
        private static readonly Logger fileProcessingLogger = LogManager.GetLogger("FileProcessingLogger");

        #region Debug Log (Dành cho nhà phát triển - Chi tiết nhất)
        public static void LogDebug(string message, [System.Runtime.CompilerServices.CallerMemberName] string memberName = "", [System.Runtime.CompilerServices.CallerFilePath] string filePath = "")
        {
#if LOG_DEBUG
            string className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            generalLogger.Debug($"[{className}.{memberName}] {message}");
#endif
        }

        // Tương thích với code cũ
        public static void GeneralLog(LogLevel logLevel, string message)
        {
            if (logLevel == LogLevel.Debug)
            {
#if LOG_DEBUG
                generalLogger.Log(logLevel, message);
#endif
            }
            else if (logLevel == LogLevel.Info)
            {
#if LOG_INFO
                generalLogger.Log(logLevel, message);
#endif
            }
            else
            {
                generalLogger.Log(logLevel, message);
            }
        }
        #endregion

        #region Info Log (Thông tin vận hành bình thường)
        public static void LogInfo(string message)
        {
#if LOG_INFO
            generalLogger.Info(message);
#endif
        }
        #endregion

        #region Warning Log (Cảnh báo không gây dừng ứng dụng)
        public static void LogWarn(string message)
        {
#if LOG_WARN
            generalLogger.Warn(message);
#endif
        }
        #endregion

        #region Error Log (Lỗi gây gián đoạn hoặc ngoại lệ kỹ thuật)
        public static void LogError(string message, Exception ex = null)
        {
#if LOG_ERROR
            if (ex != null)
            {
                generalLogger.Error(ex, message);
            }
            else
            {
                generalLogger.Error(message);
            }
#endif
        }

        /// <summary>
        /// Ghi log ngoại lệ chuyên sâu kèm thông tin ngữ cảnh
        /// </summary>
        public static void LogException(Exception ex, string contextMessage = "Exception occurred")
        {
#if LOG_ERROR
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[EXCEPTION_DETAILS] Context: {contextMessage}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
            }
            generalLogger.Error(sb.ToString());
#endif
        }
        #endregion

        #region Device & File Logs
        public static void DeviceLog(LogLevel logLevel, string deviceId, string message)
        {
            var logEvent = LogEventInfo.Create(logLevel, "DeviceLogger", message);
            logEvent.Properties["DeviceId"] = deviceId;
            deviceLogger.Log(logEvent);
        }

        public static void FileProcessingLog(LogLevel logLevel, string message)
        {
            var logEvent = LogEventInfo.Create(logLevel, "FileProcessingLogger", message);
            fileProcessingLogger.Log(logEvent);
        }
        #endregion
    }
}
