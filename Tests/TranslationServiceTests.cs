using System.Diagnostics;
using System.Threading.Tasks;
using ScreenTranslator.Translation;
using Xunit;
using Xunit.Abstractions;

namespace ScreenTranslator.Tests
{
    public class TranslationServiceTests
    {
        private readonly ITestOutputHelper _output;
        private readonly TranslationService _translationService;

        // Điền API Key của bạn vào đây để chạy Performance Test thực tế!
        private readonly string GROQ_API_KEY = "YOUR_GROQ_API_KEY_HERE"; // NHỚ ĐỪNG ĐẨY KEY THẬT LÊN GITHUB!
        private readonly string OPENAI_API_KEY = "YOUR_OPENAI_API_KEY_HERE";

        public TranslationServiceTests(ITestOutputHelper output)
        {
            _output = output;
            _translationService = new TranslationService();
        }

        [Fact]
        public async Task TranslateAsync_EmptyText_ReturnsEmptyString_WithoutCallingApi()
        {
            // Arrange
            string text = "";

            // Act
            Stopwatch sw = Stopwatch.StartNew();
            string result = await _translationService.TranslateAsync(text, apiKey: GROQ_API_KEY);
            sw.Stop();

            // Assert
            Assert.Equal(string.Empty, result);
            Assert.True(sw.ElapsedMilliseconds < 50, "Cần trả về ngay lập tức không gọi API, tốn dưới 50ms");
            _output.WriteLine($"Empty Text Test Passed: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task TranslateAsync_InvalidApiKey_ReturnsErrorMessage()
        {
            // Arrange
            string text = "Hello world";
            string invalidKey = "sk-invalid-key-123456";

            // Act
            string result = await _translationService.TranslateAsync(text, targetLang: "vi", apiKey: invalidKey, apiType: "openai", modelName: "gpt-4o-mini");

            // Assert
            Assert.True(_translationService.IsFallbackActive, "Hệ thống tự động chuyển vùng dự phòng khi lỗi API Key");
            Assert.False(string.IsNullOrWhiteSpace(result), "Kết quả dịch dự phòng không được để trống");
            _output.WriteLine($"Invalid Key Test Passed. Fallback activated successfully: {result}");
        }

        [Fact]
        public async Task PerformanceBenchmark_Groq_Llama3_IsRealTime()
        {
            // Bỏ qua test nếu chưa cấu hình Key
            if (GROQ_API_KEY == "YOUR_GROQ_API_KEY_HERE")
            {
                _output.WriteLine("Bỏ qua test vì chưa có Groq API Key.");
                return;
            }

            // Arrange
            string textToTranslate = "The quick brown fox jumps over the lazy dog. Real-time translation is a crucial feature for our application.";
            
            // Act
            Stopwatch sw = Stopwatch.StartNew();
            string result = await _translationService.TranslateAsync(textToTranslate, targetLang: "vi", apiKey: GROQ_API_KEY, apiType: "groq", modelName: "llama-3.1-8b-instant");
            sw.Stop();

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(result));
            
            // Theo như NFR (Yêu cầu phi chức năng): Threshold < 1000ms cho Model Inference.
            _output.WriteLine($"[Benchmark - Groq] Thời gian phản hồi: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"[Benchmark - Groq] Kết quả dịch: {result}");
            
            Assert.True(sw.ElapsedMilliseconds < 1000, "Groq Model Inference đã vượt quá giới hạn 1000ms, không còn là Real-time.");
        }

        [Fact]
        public async Task PerformanceBenchmark_OpenAI_Gpt4oMini_AccuracyTradeoff()
        {
            // Bỏ qua test nếu chưa cấu hình Key
            if (OPENAI_API_KEY == "YOUR_OPENAI_API_KEY_HERE")
            {
                _output.WriteLine("Bỏ qua test vì chưa có OpenAI API Key.");
                return;
            }

            // Arrange
            string textToTranslate = "The quick brown fox jumps over the lazy dog. Real-time translation is a crucial feature for our application.";
            
            // Act
            Stopwatch sw = Stopwatch.StartNew();
            string result = await _translationService.TranslateAsync(textToTranslate, targetLang: "vi", apiKey: OPENAI_API_KEY, apiType: "openai", modelName: "gpt-4o-mini");
            sw.Stop();

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(result));
            
            _output.WriteLine($"[Benchmark - OpenAI] Thời gian phản hồi: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"[Benchmark - OpenAI] Kết quả dịch: {result}");

            // OpenAI thường ưu tiên chất lượng hơn tốc độ, ngưỡng mong đợi có thể cao hơn đôi chút (~1500ms).
            Assert.True(sw.ElapsedMilliseconds < 2500, "OpenAI API trả về quá chậm (> 2.5 giây).");
        }

        [Fact]
        public async Task TranslateAsync_LocalOffline_WhenModelFilesAreMissing_ReturnsGracefulInstructionString()
        {
            // Act
            string result = await _translationService.TranslateAsync("Hello", apiType: "local_offline");

            // Assert
            Assert.Contains("Không tìm thấy tệp mô hình ngoại tuyến", result);
            _output.WriteLine($"Offline fallback instruction test passed: {result}");
        }
        [Fact]
        public async Task TranslateAsync_FreeGoogle_WhenFailed_FallsBackToOffline()
        {
            // Act
            // Using a very long string to force UriFormatException in TranslateViaFreeGoogleAsync
            string longText = new string('A', 65500);
            string result = await _translationService.TranslateAsync(longText, apiType: "free_google");

            // Assert
            Assert.True(_translationService.IsFallbackActive, "IsFallbackActive should be true when free google fails");
            Assert.True(_translationService.IsOfflineFallback, "IsOfflineFallback should be true when falling back to offline");
            Assert.Contains("Không tìm thấy tệp mô hình ngoại tuyến", result);
            _output.WriteLine($"Free Google fallback to offline test passed: {result}");
        }
    }
}
