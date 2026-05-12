using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScreenTranslator.Translation
{
    public class TranslationService
    {
        private readonly HttpClient _client;

        public TranslationService()
        {
            _client = new HttpClient();
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "auto", string targetLang = "vi", string apiKey = "", string apiType = "groq", string modelName = "llama-3.1-8b-instant")
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                return "API Key is missing.";

            try
            {
                var targetLanguageName = GetLanguageName(targetLang);
                
                string apiEndpoint = "https://api.groq.com/openai/v1/chat/completions";
                if (apiType == "openai")
                    apiEndpoint = "https://api.openai.com/v1/chat/completions";
                
                var payload = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = "You are a professional translator. Fix any OCR typos automatically and understand the context. ONLY output the translation, do not include any other conversational text or quotes." },
                        new { role = "user", content = $"Translate the following OCR text to {targetLanguageName}:\n\n{text}" }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, apiEndpoint);
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return $"API Error: {response.StatusCode} - {error}";
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                
                using (var document = JsonDocument.Parse(jsonString))
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var message = choices[0].GetProperty("message");
                        if (message.TryGetProperty("content", out var content))
                        {
                            var result = content.GetString();
                            if (result != null)
                            {
                                return result.Trim();
                            }
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation Error: {ex.Message}");
                return "Error: " + ex.Message;
            }
        }

        private string GetLanguageName(string code)
        {
            return code switch
            {
                "vi" => "Vietnamese",
                "ja" => "Japanese",
                "ko" => "Korean",
                "zh-CN" => "Simplified Chinese",
                "en" => "English",
                _ => "Vietnamese"
            };
        }
    }
}
