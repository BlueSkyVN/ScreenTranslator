using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace ScreenTranslator.Infrastructure
{
    /// <summary>
    /// Hệ thống thu thập số liệu hiệu năng (Telemetry/Metrics) thời gian thực.
    /// Theo dõi: số lần dịch, thời gian OCR/Translation trung bình, tỷ lệ cache hit, fallback, lỗi.
    /// Tất cả đều thread-safe và không phụ thuộc dịch vụ bên ngoài (chạy hoàn toàn nội bộ).
    /// </summary>
    public sealed class TelemetryService
    {
        private static readonly Lazy<TelemetryService> _instance = new(() => new TelemetryService());
        public static TelemetryService Instance => _instance.Value;

        // Bộ đếm nguyên tử (Atomic Counters)
        private long _totalTranslations = 0;
        private long _totalOcrCalls = 0;
        private long _cacheHits = 0;
        private long _cacheMisses = 0;
        private long _fallbackCount = 0;
        private long _errorCount = 0;
        private long _totalOcrTimeMs = 0;
        private long _totalTranslationTimeMs = 0;
        private long _totalCaptureTimeMs = 0;
        private long _skippedBySimilarity = 0;

        // Thời điểm bắt đầu để tính uptime
        private readonly DateTime _startTime;

        private TelemetryService()
        {
            _startTime = DateTime.Now;
        }

        // --- Ghi nhận sự kiện ---

        public void RecordTranslation(long elapsedMs)
        {
            Interlocked.Increment(ref _totalTranslations);
            Interlocked.Add(ref _totalTranslationTimeMs, elapsedMs);
        }

        public void RecordOcr(long elapsedMs)
        {
            Interlocked.Increment(ref _totalOcrCalls);
            Interlocked.Add(ref _totalOcrTimeMs, elapsedMs);
        }

        public void RecordCapture(long elapsedMs)
        {
            Interlocked.Add(ref _totalCaptureTimeMs, elapsedMs);
        }

        public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
        public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);
        public void RecordFallback() => Interlocked.Increment(ref _fallbackCount);
        public void RecordError() => Interlocked.Increment(ref _errorCount);
        public void RecordSkippedBySimilarity() => Interlocked.Increment(ref _skippedBySimilarity);

        // --- Đọc số liệu ---

        public long TotalTranslations => Interlocked.Read(ref _totalTranslations);
        public long TotalOcrCalls => Interlocked.Read(ref _totalOcrCalls);
        public long CacheHits => Interlocked.Read(ref _cacheHits);
        public long CacheMisses => Interlocked.Read(ref _cacheMisses);
        public long FallbackCount => Interlocked.Read(ref _fallbackCount);
        public long ErrorCount => Interlocked.Read(ref _errorCount);
        public long SkippedBySimilarity => Interlocked.Read(ref _skippedBySimilarity);

        /// <summary>
        /// Tỷ lệ cache hit (0.0 - 1.0). Chỉ số càng cao = càng ít gọi API lãng phí.
        /// </summary>
        public double CacheHitRate
        {
            get
            {
                long total = CacheHits + CacheMisses;
                return total == 0 ? 0.0 : (double)CacheHits / total;
            }
        }

        /// <summary>
        /// Thời gian OCR trung bình mỗi frame (ms).
        /// </summary>
        public double AverageOcrTimeMs
        {
            get
            {
                long calls = TotalOcrCalls;
                return calls == 0 ? 0 : (double)Interlocked.Read(ref _totalOcrTimeMs) / calls;
            }
        }

        /// <summary>
        /// Thời gian dịch thuật trung bình mỗi lần gọi (ms).
        /// </summary>
        public double AverageTranslationTimeMs
        {
            get
            {
                long calls = TotalTranslations;
                return calls == 0 ? 0 : (double)Interlocked.Read(ref _totalTranslationTimeMs) / calls;
            }
        }

        /// <summary>
        /// Thời gian chụp màn hình trung bình (ms).
        /// </summary>
        public double AverageCaptureTimeMs
        {
            get
            {
                long calls = TotalOcrCalls; // Capture luôn đi kèm OCR
                return calls == 0 ? 0 : (double)Interlocked.Read(ref _totalCaptureTimeMs) / calls;
            }
        }

        /// <summary>
        /// Tổng thời gian ứng dụng đã chạy.
        /// </summary>
        public TimeSpan Uptime => DateTime.Now - _startTime;

        /// <summary>
        /// Tạo bản tóm tắt toàn bộ metrics dạng chuỗi để hiển thị hoặc ghi log.
        /// </summary>
        public string GetSummary()
        {
            return $"=== Telemetry Summary ===\n" +
                   $"Uptime: {Uptime:hh\\:mm\\:ss}\n" +
                   $"Total OCR calls: {TotalOcrCalls}\n" +
                   $"Total Translations: {TotalTranslations}\n" +
                   $"Cache Hit Rate: {CacheHitRate:P1} ({CacheHits} hits / {CacheMisses} misses)\n" +
                   $"Skipped by Similarity: {SkippedBySimilarity}\n" +
                   $"Fallbacks: {FallbackCount}\n" +
                   $"Errors: {ErrorCount}\n" +
                   $"Avg Capture Time: {AverageCaptureTimeMs:F1} ms\n" +
                   $"Avg OCR Time: {AverageOcrTimeMs:F1} ms\n" +
                   $"Avg Translation Time: {AverageTranslationTimeMs:F1} ms\n" +
                   $"========================";
        }

        /// <summary>
        /// Tiện ích: Đo thời gian thực thi một đoạn code và trả về kết quả kèm elapsed time.
        /// </summary>
        public static (T Result, long ElapsedMs) Measure<T>(Func<T> action)
        {
            var sw = Stopwatch.StartNew();
            T result = action();
            sw.Stop();
            return (result, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Tiện ích: Đo thời gian thực thi một Task async.
        /// </summary>
        public static async System.Threading.Tasks.Task<(T Result, long ElapsedMs)> MeasureAsync<T>(Func<System.Threading.Tasks.Task<T>> action)
        {
            var sw = Stopwatch.StartNew();
            T result = await action();
            sw.Stop();
            return (result, sw.ElapsedMilliseconds);
        }
    }
}
