using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator.Infrastructure
{
    /// <summary>
    /// Hệ thống Logging trung tâm cho toàn bộ ứng dụng.
    /// Hỗ trợ ghi log ra file với xoay vòng (log rotation), đa luồng an toàn (thread-safe),
    /// và phân loại mức độ nghiêm trọng (Severity Level).
    /// </summary>
    public sealed class LogService : IDisposable
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error,
            Fatal
        }

        private static readonly Lazy<LogService> _instance = new(() => new LogService());
        public static LogService Instance => _instance.Value;

        private readonly string _logDirectory;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _writerTask;
        private readonly long _maxFileSizeBytes;
        private string _currentLogFilePath;
        private LogLevel _minimumLevel = LogLevel.Debug;

        /// <summary>
        /// Sự kiện phát ra khi có log mới, cho phép UI subscribe để hiển thị log trực tiếp.
        /// </summary>
        public event Action<string>? OnLogEntry;

        private LogService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _maxFileSizeBytes = 5 * 1024 * 1024; // 5 MB mỗi file log tối đa

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            _currentLogFilePath = GetLogFilePath();
            _writerTask = Task.Run(() => ProcessLogQueueAsync(_cts.Token));

            Info("LogService", "=== Logging System Initialized ===");
            Info("LogService", $"Log directory: {_logDirectory}");
            Info("LogService", $"App version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Info("LogService", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}");
        }

        /// <summary>
        /// Thiết lập mức log tối thiểu. Các log có level thấp hơn sẽ bị bỏ qua.
        /// </summary>
        public void SetMinimumLevel(LogLevel level)
        {
            _minimumLevel = level;
        }

        public void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
        public void Info(string source, string message) => Log(LogLevel.Info, source, message);
        public void Warning(string source, string message) => Log(LogLevel.Warning, source, message);
        public void Error(string source, string message, Exception? ex = null)
        {
            string fullMessage = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}" : message;
            Log(LogLevel.Error, source, fullMessage);
        }
        public void Fatal(string source, string message, Exception? ex = null)
        {
            string fullMessage = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
            Log(LogLevel.Fatal, source, fullMessage);
        }

        private void Log(LogLevel level, string source, string message)
        {
            if (level < _minimumLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelTag = level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Fatal => "FTL",
                _ => "???"
            };

            string entry = $"[{timestamp}] [{levelTag}] [{source}] {message}";
            _logQueue.Enqueue(entry);
            OnLogEntry?.Invoke(entry);
        }

        private async Task ProcessLogQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_logQueue.IsEmpty)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }

                    // Xoay vòng file log nếu kích thước vượt quá giới hạn
                    RotateLogFileIfNeeded();

                    var sb = new StringBuilder();
                    while (_logQueue.TryDequeue(out string? entry))
                    {
                        sb.AppendLine(entry);
                    }

                    if (sb.Length > 0)
                    {
                        await File.AppendAllTextAsync(_currentLogFilePath, sb.ToString(), token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Logging system không được phép crash ứng dụng
                }
            }

            // Flush remaining logs trước khi shutdown
            FlushRemainingLogs();
        }

        private void FlushRemainingLogs()
        {
            try
            {
                var sb = new StringBuilder();
                while (_logQueue.TryDequeue(out string? entry))
                {
                    sb.AppendLine(entry);
                }
                if (sb.Length > 0)
                {
                    File.AppendAllText(_currentLogFilePath, sb.ToString());
                }
            }
            catch { }
        }

        private void RotateLogFileIfNeeded()
        {
            try
            {
                if (File.Exists(_currentLogFilePath))
                {
                    var info = new FileInfo(_currentLogFilePath);
                    if (info.Length >= _maxFileSizeBytes)
                    {
                        _currentLogFilePath = GetLogFilePath();
                    }
                }

                // Dọn dẹp log cũ quá 7 ngày
                CleanOldLogs(7);
            }
            catch { }
        }

        private void CleanOldLogs(int retentionDays)
        {
            try
            {
                foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
                {
                    if (File.GetCreationTime(file) < DateTime.Now.AddDays(-retentionDays))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch { }
        }

        private string GetLogFilePath()
        {
            return Path.Combine(_logDirectory, $"screentranslator_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        /// <summary>
        /// Trả về đường dẫn thư mục chứa log files.
        /// </summary>
        public string GetLogDirectory() => _logDirectory;

        public void Dispose()
        {
            Info("LogService", "=== Logging System Shutting Down ===");
            _cts.Cancel();
            try { _writerTask.Wait(2000); } catch { }
            _cts.Dispose();
        }
    }
}
