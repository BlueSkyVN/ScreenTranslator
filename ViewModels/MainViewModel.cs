using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScreenTranslator.Capture;
using ScreenTranslator.Ocr;
using ScreenTranslator.Translation;
using ScreenTranslator.UI;

namespace ScreenTranslator.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly ScreenCaptureService _captureService;
        private readonly OcrService _ocrService;
        private readonly TranslationService _translationService;
        private OverlayWindow? _overlay;

        private Rectangle _captureRegion;
        private CancellationTokenSource? _cts;

        private const string ConfigFileName = "config.json";
        private readonly Dictionary<string, string> _translationCache = new();

        public MainViewModel()
        {
            _captureService = new ScreenCaptureService();
            _ocrService = new OcrService("en-US");
            _translationService = new TranslationService();

            SelectRegionCommand = new RelayCommand(_ => SelectRegion());
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync());

            // Default capture region (bottom 30% of screen)
            int screenW = (int)SystemParameters.PrimaryScreenWidth;
            int screenH = (int)SystemParameters.PrimaryScreenHeight;
            int captureHeight = (int)(screenH * 0.3);
            _captureRegion = new Rectangle(0, screenH - captureHeight, screenW, captureHeight);

            // Open overlay by default
            _overlay = new OverlayWindow();
            
            LoadSettings(); // Load persisted settings from local JSON config

            // Apply loaded settings to overlay
            _overlay.SetOpacity(OverlayOpacity);
            _overlay.SetFontSize(OverlayFontSize);
            _overlay.SetTextColor(OverlayTextColor);
            _overlay.SetClickThrough(LockOverlayClickThrough);

            _overlay.Show();
        }

        private string _targetLang = "vi";
        public string TargetLang
        {
            get => _targetLang;
            set
            {
                if (SetProperty(ref _targetLang, value))
                {
                    _overlay?.UpdateText(""); // Clear text on lang change
                    SaveSettings();
                }
            }
        }

        private string _apiKey = "";
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                {
                    SaveSettings();
                }
            }
        }

        private string _apiModel = "groq|llama-3.1-8b-instant";
        public string ApiModel
        {
            get => _apiModel;
            set
            {
                if (SetProperty(ref _apiModel, value))
                {
                    SaveSettings();
                }
            }
        }

        private double _overlayOpacity = 0.8;
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set
            {
                if (SetProperty(ref _overlayOpacity, value))
                {
                    _overlay?.SetOpacity(value);
                    SaveSettings();
                }
            }
        }

        private double _overlayFontSize = 26;
        public double OverlayFontSize
        {
            get => _overlayFontSize;
            set
            {
                if (SetProperty(ref _overlayFontSize, value))
                {
                    _overlay?.SetFontSize(value);
                    SaveSettings();
                }
            }
        }

        private string _overlayTextColor = "#00FF00";
        public string OverlayTextColor
        {
            get => _overlayTextColor;
            set
            {
                if (SetProperty(ref _overlayTextColor, value))
                {
                    _overlay?.SetTextColor(value);
                    SaveSettings();
                }
            }
        }

        private bool _lockOverlayClickThrough = false;
        public bool LockOverlayClickThrough
        {
            get => _lockOverlayClickThrough;
            set
            {
                if (SetProperty(ref _lockOverlayClickThrough, value))
                {
                    _overlay?.SetClickThrough(value);
                    SaveSettings();
                }
            }
        }

        private bool _isRunning = false;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    StatusText = value ? "Running" : "Stopped";
                    StatusColor = value ? "Green" : "Red";

                    if (value)
                    {
                        string apiType = "groq";
                        var parts = ApiModel.Split('|');
                        if (parts.Length == 2)
                        {
                            apiType = parts[0];
                        }

                        if (apiType != "free_google" && apiType != "local_offline" && string.IsNullOrWhiteSpace(ApiKey))
                        {
                            MessageBox.Show("Vui lòng nhập API Key trước khi bắt đầu!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                            Application.Current.Dispatcher.InvokeAsync(() => IsRunning = false);
                            return;
                        }

                        _cts = new CancellationTokenSource();
                        Task.Run(() => TranslationLoop(_cts.Token));
                    }
                    else
                    {
                        _cts?.Cancel();
                        _overlay?.UpdateText("");
                    }
                }
            }
        }

        private string _statusText = "Stopped";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _statusColor = "Red";
        public string StatusColor
        {
            get => _statusColor;
            set => SetProperty(ref _statusColor, value);
        }

        private string _lastOcrText = "-";
        public string LastOcrText
        {
            get => _lastOcrText;
            set => SetProperty(ref _lastOcrText, value);
        }

        private string _connectionStatusText = "Chưa kiểm tra";
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set => SetProperty(ref _connectionStatusText, value);
        }

        private string _connectionStatusColor = "Gray";
        public string ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set => SetProperty(ref _connectionStatusColor, value);
        }

        public ICommand SelectRegionCommand { get; }
        public ICommand TestConnectionCommand { get; }

        private void SelectRegion()
        {
            var regionWindow = new RegionSelectionWindow();
            if (regionWindow.ShowDialog() == true && !regionWindow.SelectedRegion.IsEmpty)
            {
                _captureRegion = regionWindow.SelectedRegion;
                SaveSettings();
                MessageBox.Show($"Vùng chọn đã được cập nhật:\nTọa độ: ({_captureRegion.X}, {_captureRegion.Y})\nKích thước: {_captureRegion.Width}x{_captureRegion.Height}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task TestConnectionAsync()
        {
            string apiType = "groq";
            string modelName = "llama-3.1-8b-instant";
            var parts = ApiModel.Split('|');
            if (parts.Length == 2)
            {
                apiType = parts[0];
                modelName = parts[1];
            }

            if (apiType == "free_google" || apiType == "local_offline")
            {
                ConnectionStatusText = "Kết nối thành công! (Không cần Key)";
                ConnectionStatusColor = "Green";
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                ConnectionStatusText = "Lỗi: Chưa nhập API Key";
                ConnectionStatusColor = "Red";
                return;
            }

            ConnectionStatusText = "Đang kết nối...";
            ConnectionStatusColor = "Orange";

            var result = await _translationService.TranslateAsync("Hello", "en", TargetLang, ApiKey, apiType, modelName);
            if (result.StartsWith("API Error:") || result.StartsWith("Error:") || result.StartsWith("Free Google Translate Error:"))
            {
                ConnectionStatusText = "Thất bại (Sai Key hoặc lỗi mạng)";
                ConnectionStatusColor = "Red";
            }
            else
            {
                ConnectionStatusText = "Kết nối thành công!";
                ConnectionStatusColor = "Green";
            }
        }

        private async Task TranslationLoop(CancellationToken token)
        {
            string lastText = "";
            string apiType = "";
            string modelName = "";

            var parts = ApiModel.Split('|');
            if (parts.Length == 2)
            {
                apiType = parts[0];
                modelName = parts[1];
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var softwareBitmap = await _captureService.CaptureRegionAsync(_captureRegion);
                    if (softwareBitmap != null)
                    {
                        var text = await _ocrService.RecognizeTextAsync(softwareBitmap);

                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            LastOcrText = string.IsNullOrWhiteSpace(text) ? "(Không tìm thấy chữ nào trong vùng này)" : text;
                        });

                        if (!string.IsNullOrWhiteSpace(text) && text != lastText)
                        {
                            // 1. Tối ưu 1: So khớp độ tương đồng mờ (Fuzzy OCR Similarity) để tránh spam API khi chữ biến động nhẹ
                            if (CalculateSimilarity(text, lastText) >= 0.92)
                            {
                                // Xem như chữ không đổi để tránh gọi dịch
                                lastText = text;
                                await Task.Delay(1000, token);
                                continue;
                            }

                            // 2. Tối ưu 2: Tra cứu cache trước khi gọi API dịch thuật
                            string cacheKey = $"{TargetLang}|{apiType}|{modelName}|{text.Trim().ToLower()}";
                            string translated;

                            if (_translationCache.TryGetValue(cacheKey, out string? cachedTranslated))
                            {
                                translated = cachedTranslated;
                            }
                            else
                            {
                                translated = await _translationService.TranslateAsync(text, "en", TargetLang, ApiKey, apiType, modelName);
                                if (!translated.StartsWith("API Error:") && !translated.StartsWith("Error:") && !translated.StartsWith("Free Google Translate Error:"))
                                {
                                    _translationCache[cacheKey] = translated;
                                }
                            }

                            _overlay?.UpdateText(translated);
                            lastText = text;

                            bool isFallback = _translationService.IsFallbackActive;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (isFallback)
                                {
                                    StatusText = "Dự phòng (AI Quá tải -> Google)";
                                    StatusColor = "Orange";
                                }
                                else if (IsRunning)
                                {
                                    StatusText = "Running (AI Engine)";
                                    StatusColor = "Green";
                                }
                            });
                        }
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() => LastOcrText = "Chụp màn hình thất bại.");
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => LastOcrText = "Lỗi: " + ex.Message);
                }

                await Task.Delay(1000, token);
            }
        }

        #region Thuật toán Fuzzy OCR String Similarity (Levenshtein Distance)
        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;
            if (source == target) return 1.0;

            int stepsToSame = LevenshteinDistance(source, target);
            return 1.0 - ((double)stepsToSame / Math.Max(source.Length, target.Length));
        }

        private int LevenshteinDistance(string source, string target)
        {
            int n = source.Length;
            int m = target.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
        #endregion

        #region Tự động Lưu & Tải Cấu hình (JSON config.json)
        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    ApiKey = ApiKey,
                    ApiModel = ApiModel,
                    TargetLang = TargetLang,
                    OverlayOpacity = OverlayOpacity,
                    OverlayFontSize = OverlayFontSize,
                    OverlayTextColor = OverlayTextColor,
                    LockOverlayClickThrough = LockOverlayClickThrough,
                    CaptureRegion = new { X = _captureRegion.X, Y = _captureRegion.Y, Width = _captureRegion.Width, Height = _captureRegion.Height }
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("ApiKey", out var apiKeyProp)) ApiKey = apiKeyProp.GetString() ?? "";
                        if (root.TryGetProperty("ApiModel", out var apiModelProp)) ApiModel = apiModelProp.GetString() ?? "groq|llama-3.1-8b-instant";
                        if (root.TryGetProperty("TargetLang", out var targetLangProp)) TargetLang = targetLangProp.GetString() ?? "vi";
                        if (root.TryGetProperty("OverlayOpacity", out var opacityProp)) OverlayOpacity = opacityProp.GetDouble();
                        if (root.TryGetProperty("OverlayFontSize", out var fontSizeProp)) OverlayFontSize = fontSizeProp.GetDouble();
                        if (root.TryGetProperty("OverlayTextColor", out var colorProp)) OverlayTextColor = colorProp.GetString() ?? "#00FF00";
                        if (root.TryGetProperty("LockOverlayClickThrough", out var lockProp)) LockOverlayClickThrough = lockProp.GetBoolean();

                        if (root.TryGetProperty("CaptureRegion", out var regionProp))
                        {
                            int x = regionProp.GetProperty("X").GetInt32();
                            int y = regionProp.GetProperty("Y").GetInt32();
                            int w = regionProp.GetProperty("Width").GetInt32();
                            int h = regionProp.GetProperty("Height").GetInt32();
                            _captureRegion = new Rectangle(x, y, w, h);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }
        }
        #endregion

        public void Dispose()
        {
            _cts?.Cancel();
            _overlay?.Close();
        }
    }
}
