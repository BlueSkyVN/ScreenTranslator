using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScreenTranslator.Translation
{
    public class TranslationService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly OfflineTranslationEngine _offlineEngine;
        
        public bool IsFallbackActive { get; set; } = false;

        public TranslationService()
        {
            _client = new HttpClient();
            _offlineEngine = new OfflineTranslationEngine();
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "auto", string targetLang = "vi", string apiKey = "", string apiType = "groq", string modelName = "llama-3.1-8b-instant")
        {
            IsFallbackActive = false; // Reset fallback status for this request

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (apiType == "free_google")
            {
                return await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
            }

            if (apiType == "local_offline")
            {
                return await _offlineEngine.TranslateAsync(text);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // If API Key is missing, treat as immediate failover instead of returning error
                Console.WriteLine("API Key is missing. Failing over immediately to Free Google Translate.");
                IsFallbackActive = true;
                return await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
            }

            try
            {
                var targetLanguageName = GetLanguageName(targetLang);
                
                string apiEndpoint = "https://api.groq.com/openai/v1/chat/completions";
                if (apiType == "openai")
                    apiEndpoint = "https://api.openai.com/v1/chat/completions";
                else if (apiType == "gemini")
                    apiEndpoint = "https://generativelanguage.googleapis.com/v1beta/chat/completions";
                
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
                    Console.WriteLine($"API Error {response.StatusCode}: {error}. Automatically falling back to Free Google Translate.");
                    IsFallbackActive = true;
                    return await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
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
                Console.WriteLine($"Translation Engine Error: {ex.Message}. Automatically falling back to Free Google Translate.");
                IsFallbackActive = true;
                return await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
            }
        }

        public async Task<string> TranslateViaFreeGoogleAsync(string text, string sourceLang = "auto", string targetLang = "vi")
        {
            try
            {
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang}&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
                var response = await _client.GetStringAsync(url);
                
                using (var document = JsonDocument.Parse(response))
                {
                    var root = document.RootElement;
                    var arr = root[0];
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < arr.GetArrayLength(); i++)
                    {
                        sb.Append(arr[i][0].GetString());
                    }
                    return sb.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                return $"Free Google Translate Error: {ex.Message}";
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

        public void Dispose()
        {
            _offlineEngine?.Dispose();
            _client?.Dispose();
        }
    }
}
