using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ScreenTranslator.Infrastructure;

namespace ScreenTranslator.Translation
{
    public class TranslationService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly OfflineTranslationEngine _offlineEngine;
        private readonly LogService _log = LogService.Instance;
        
        public bool IsFallbackActive { get; set; } = false;
        public bool IsOfflineFallback { get; set; } = false;

        public TranslationService()
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(10); // Fix #10: Timeout API call để tránh treo vô hạn
            _offlineEngine = new OfflineTranslationEngine();
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "auto", string targetLang = "vi", string apiKey = "", string apiType = "groq", string modelName = "llama-3.1-8b-instant")
        {
            IsFallbackActive = false; // Reset fallback status for this request
            IsOfflineFallback = false;

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (apiType == "free_google")
            {
                var result = await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
                if (result.StartsWith("Free Google Translate Error:"))
                {
                    _log.Warning("TranslationService", $"Free Google Translate failed: {result}. Attempting fallback to ONNX Offline.");
                    IsFallbackActive = true;
                    IsOfflineFallback = true;
                    return await _offlineEngine.TranslateAsync(text);
                }
                return result;
            }

            if (apiType == "local_offline")
            {
                return await _offlineEngine.TranslateAsync(text);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // Path A: API Key missing → failover cascade (Google → Offline)
                _log.Warning("TranslationService", "API Key is missing. Initiating failover cascade.");
                IsFallbackActive = true;
                return await FallbackWithOfflineAsync(text, sourceLang, targetLang);
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
                    // Path B: HTTP error (401/429/503) → failover cascade
                    var error = await response.Content.ReadAsStringAsync();
                    _log.Warning("TranslationService", $"API Error {response.StatusCode}: {error}. Initiating failover cascade.");
                    IsFallbackActive = true;
                    return await FallbackWithOfflineAsync(text, sourceLang, targetLang);
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
                // Path C: Exception (timeout/DNS/malformed) → failover cascade
                _log.Error("TranslationService", $"Translation Engine Error: {ex.Message}. Initiating failover cascade.");
                IsFallbackActive = true;
                return await FallbackWithOfflineAsync(text, sourceLang, targetLang);
            }
        }

        /// <summary>
        /// Two-tier failover cascade: Google Translate (cloud) → ONNX Offline (local).
        /// If Google Translate succeeds, returns its result.
        /// If Google Translate also fails (e.g., no network), automatically routes to
        /// the local ONNX offline engine if it has been initialized.
        /// </summary>
        private async Task<string> FallbackWithOfflineAsync(string text, string sourceLang, string targetLang)
        {
            try
            {
                // Tier 1: Try Google Translate (free, cloud-based)
                var result = await TranslateViaFreeGoogleAsync(text, sourceLang, targetLang);
                
                // Check if Google returned an error string (not a real translation)
                if (!result.StartsWith("Free Google Translate Error:"))
                {
                    _log.Info("TranslationService", "Failover Tier 1 succeeded: Google Translate.");
                    return result;
                }

                // Google also failed — fall through to Tier 2
                _log.Warning("TranslationService", $"Google Translate also failed: {result}. Attempting Tier 2: ONNX Offline.");
            }
            catch (Exception ex)
            {
                _log.Warning("TranslationService", $"Google Translate exception: {ex.Message}. Attempting Tier 2: ONNX Offline.");
            }

            // Tier 2: Try local ONNX offline engine
            try
            {
                if (_offlineEngine.IsInitialized)
                {
                    IsOfflineFallback = true;
                    var offlineResult = await _offlineEngine.TranslateAsync(text);
                    _log.Info("TranslationService", "Failover Tier 2 succeeded: ONNX Offline engine.");
                    return offlineResult;
                }
                else
                {
                    // Attempt to initialize the offline engine on-demand
                    bool initialized = _offlineEngine.Initialize();
                    if (initialized)
                    {
                        IsOfflineFallback = true;
                        var offlineResult = await _offlineEngine.TranslateAsync(text);
                        _log.Info("TranslationService", "Failover Tier 2 succeeded: ONNX Offline engine (lazy-initialized).");
                        return offlineResult;
                    }
                }
            }
            catch (Exception offlineEx)
            {
                _log.Error("TranslationService", $"ONNX Offline engine error: {offlineEx.Message}");
            }

            // All tiers exhausted
            _log.Error("TranslationService", "All failover tiers exhausted. No translation available.");
            return "[Translation unavailable — all engines failed. Check network or install offline model.]";
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
