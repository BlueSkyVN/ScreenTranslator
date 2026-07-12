using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScreenTranslator.Capture;
using ScreenTranslator.Infrastructure;
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
        private readonly LogService _log = LogService.Instance;
        private readonly TelemetryService _telemetry = TelemetryService.Instance;
        private readonly ProfileManager _profileManager = new();
        private OverlayWindow? _overlay;

        private Rectangle _captureRegion;
        private CancellationTokenSource? _cts;

        private const string ConfigFileName = "config.json";
        private const int MaxCacheSize = 500;
        private readonly Dictionary<string, string> _translationCache = new();
        private CancellationTokenSource? _saveDebounceTokenSource;

        public MainViewModel()
        {
            _captureService = new ScreenCaptureService();
            _ocrService = new OcrService("en-US");
            _translationService = new TranslationService();

            SelectRegionCommand = new RelayCommand(_ => SelectRegion());
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
            ShowTelemetryCommand = new RelayCommand(_ => ShowTelemetrySummary());
            SaveProfileCommand = new RelayCommand(_ => SaveCurrentProfile());
            LoadProfileCommand = new RelayCommand(_ => LoadSelectedProfile());

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

            _log.Info("MainViewModel", "Application started successfully.");
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
        public ICommand OpenLogFolderCommand { get; }
        public ICommand ShowTelemetryCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand LoadProfileCommand { get; }

        // --- Telemetry display properties ---
        private string _telemetrySummary = "Chưa có dữ liệu.";
        public string TelemetrySummary
        {
            get => _telemetrySummary;
            set => SetProperty(ref _telemetrySummary, value);
        }

        // --- Profile properties ---
        private string _currentProfileName = "Default";
        public string CurrentProfileName
        {
            get => _currentProfileName;
            set => SetProperty(ref _currentProfileName, value);
        }

        private void SelectRegion()
        {
            var regionWindow = new RegionSelectionWindow();
            if (regionWindow.ShowDialog() == true && !regionWindow.SelectedRegion.IsEmpty)
            {
                _captureRegion = regionWindow.SelectedRegion;
                SaveSettings();
                _log.Info("MainViewModel", $"Capture region updated: ({_captureRegion.X}, {_captureRegion.Y}) {_captureRegion.Width}x{_captureRegion.Height}");
                MessageBox.Show($"Vùng chọn đã được cập nhật:\nTọa độ: ({_captureRegion.X}, {_captureRegion.Y})\nKích thước: {_captureRegion.Width}x{_captureRegion.Height}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                string logDir = _log.GetLogDirectory();
                if (Directory.Exists(logDir))
                    Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Error("MainViewModel", "Failed to open log folder", ex);
            }
        }

        private void ShowTelemetrySummary()
        {
            TelemetrySummary = _telemetry.GetSummary();
            _log.Info("MainViewModel", "Telemetry summary requested.");
        }

        private void SaveCurrentProfile()
        {
            var profile = new ProfileManager.ProfileData
            {
                Name = CurrentProfileName,
                ApiKey = ApiKey,
                ApiModel = ApiModel,
                TargetLang = TargetLang,
                OverlayOpacity = OverlayOpacity,
                OverlayFontSize = OverlayFontSize,
                OverlayTextColor = OverlayTextColor,
                LockOverlayClickThrough = LockOverlayClickThrough,
                CaptureRegion = new ProfileManager.CaptureRegionData
                {
                    X = _captureRegion.X, Y = _captureRegion.Y,
                    Width = _captureRegion.Width, Height = _captureRegion.Height
                }
            };
            if (_profileManager.SaveProfile(profile))
                MessageBox.Show($"Profile '{CurrentProfileName}' đã được lưu!", "Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadSelectedProfile()
        {
            var profile = _profileManager.LoadProfile(CurrentProfileName);
            if (profile != null)
            {
                ApiKey = profile.ApiKey;
                ApiModel = profile.ApiModel;
                TargetLang = profile.TargetLang;
                OverlayOpacity = profile.OverlayOpacity;
                OverlayFontSize = profile.OverlayFontSize;
                OverlayTextColor = profile.OverlayTextColor;
                LockOverlayClickThrough = profile.LockOverlayClickThrough;
                if (profile.CaptureRegion != null)
                {
                    _captureRegion = new Rectangle(
                        profile.CaptureRegion.X, profile.CaptureRegion.Y,
                        profile.CaptureRegion.Width, profile.CaptureRegion.Height);
                }
                _log.Info("MainViewModel", $"Profile '{CurrentProfileName}' loaded and applied.");
                MessageBox.Show($"Profile '{CurrentProfileName}' đã được tải!", "Profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Không tìm thấy profile '{CurrentProfileName}'.", "Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            _log.Info("TranslationLoop", "Translation loop started.");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    string apiType = "groq";
                    string modelName = "llama-3.1-8b-instant";
                    var parts = ApiModel.Split('|');
                    if (parts.Length == 2)
                    {
                        apiType = parts[0];
                        modelName = parts[1];
                    }

                    // --- Capture với đo thời gian ---
                    var captureSw = Stopwatch.StartNew();
                    var softwareBitmap = await _captureService.CaptureRegionAsync(_captureRegion);
                    captureSw.Stop();
                    _telemetry.RecordCapture(captureSw.ElapsedMilliseconds);

                    if (softwareBitmap != null)
                    {
                        // --- OCR với đo thời gian ---
                        var ocrSw = Stopwatch.StartNew();
                        var text = await _ocrService.RecognizeTextAsync(softwareBitmap);
                        ocrSw.Stop();
                        softwareBitmap.Dispose();
                        _telemetry.RecordOcr(ocrSw.ElapsedMilliseconds);

                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            LastOcrText = string.IsNullOrWhiteSpace(text) ? "(Không tìm thấy chữ nào trong vùng này)" : text;
                        });

                        if (!string.IsNullOrWhiteSpace(text) && text != lastText)
                        {
                            // 1. Tối ưu 1: So khớp độ tương đồng mờ (Fuzzy OCR Similarity)
                            if (CalculateSimilarity(text, lastText) >= 0.92)
                            {
                                _telemetry.RecordSkippedBySimilarity();
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
                                _telemetry.RecordCacheHit();
                                _log.Debug("TranslationLoop", "Cache hit — skipping API call.");
                            }
                            else
                            {
                                _telemetry.RecordCacheMiss();

                                // --- Translation với đo thời gian ---
                                var translateSw = Stopwatch.StartNew();
                                translated = await _translationService.TranslateAsync(text, "en", TargetLang, ApiKey, apiType, modelName);
                                translateSw.Stop();
                                _telemetry.RecordTranslation(translateSw.ElapsedMilliseconds);
                                _log.Info("TranslationLoop", $"Translation completed in {translateSw.ElapsedMilliseconds}ms via {apiType}/{modelName}");

                                if (!translated.StartsWith("API Error:") && !translated.StartsWith("Error:") && !translated.StartsWith("Free Google Translate Error:"))
                                {
                                    if (_translationCache.Count >= MaxCacheSize)
                                    {
                                        _translationCache.Clear();
                                        _log.Debug("TranslationLoop", "Cache cleared (reached max size).");
                                    }
                                    _translationCache[cacheKey] = translated;
                                }
                            }

                            _overlay?.UpdateText(translated);
                            lastText = text;

                            bool isFallback = _translationService.IsFallbackActive;
                            if (isFallback) _telemetry.RecordFallback();

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (isFallback)
                                {
                                    if (_translationService.IsOfflineFallback)
                                    {
                                        StatusText = "Dự phòng (Ngoại tuyến)";
                                        ApiModel = "local_offline|none";
                                    }
                                    else
                                    {
                                        StatusText = "Dự phòng (AI Quá tải -> Google)";
                                    }
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
                        _log.Warning("TranslationLoop", "Screen capture returned null.");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _telemetry.RecordError();
                    _log.Error("TranslationLoop", "Error in translation loop", ex);
                    Application.Current.Dispatcher.Invoke(() => LastOcrText = "Lỗi: " + ex.Message);
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _log.Info("TranslationLoop", "Translation loop stopped.");
            _log.Info("TranslationLoop", _telemetry.GetSummary());
        }

        #region Thuật toán Fuzzy OCR String Similarity (Levenshtein Distance)
        private double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;
            if (source == target) return 1.0;

            int stepsToSame = LevenshteinDistance(source, target);
            return 1.0 - ((double)stepsToSame / Math.Max(source.Length, target.Length));
        }

        /// <summary>
        /// Fix #3: Tối ưu Levenshtein Distance từ O(n×m) memory xuống O(m) memory.
        /// Chỉ dùng 2 mảng 1D xoay vòng thay vì ma trận 2D đầy đủ.
        /// </summary>
        private int LevenshteinDistance(string source, string target)
        {
            int n = source.Length;
            int m = target.Length;

            if (n == 0) return m;
            if (m == 0) return n;

            // Đảm bảo m là chiều ngắn hơn để tiết kiệm bộ nhớ tối đa
            if (n < m)
            {
                (source, target) = (target, source);
                (n, m) = (m, n);
            }

            int[] previousRow = new int[m + 1];
            int[] currentRow = new int[m + 1];

            for (int j = 0; j <= m; j++)
                previousRow[j] = j;

            for (int i = 1; i <= n; i++)
            {
                currentRow[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    currentRow[j] = Math.Min(
                        Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                        previousRow[j - 1] + cost);
                }
                // Hoán đổi 2 hàng
                (previousRow, currentRow) = (currentRow, previousRow);
            }
            return previousRow[m];
        }
        #endregion

        #region Tự động Lưu & Tải Cấu hình (JSON config.json)
        /// <summary>
        /// Fix #8: Debounce SaveSettings — chỉ thực sự ghi file sau 500ms không có thay đổi mới.
        /// Tránh ghi file liên tục khi người dùng kéo slider.
        /// </summary>
        private void SaveSettings()
        {
            _saveDebounceTokenSource?.Cancel();
            _saveDebounceTokenSource = new CancellationTokenSource();
            var debounceToken = _saveDebounceTokenSource.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, debounceToken);
                    SaveSettingsImmediate();
                }
                catch (OperationCanceledException) { /* Debounce bị hủy bởi thay đổi mới */ }
            });
        }

        private void SaveSettingsImmediate()
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
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                _log.Error("Settings", "Error saving settings", ex);
            }
        }

        private void LoadSettings()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
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
                _log.Error("Settings", "Error loading settings", ex);
            }
        }
        #endregion

        public void Dispose()
        {
            _log.Info("MainViewModel", "Application shutting down...");
            _log.Info("MainViewModel", _telemetry.GetSummary());
            _cts?.Cancel();
            _saveDebounceTokenSource?.Cancel();
            _translationService.Dispose();
            _log.Dispose();
            _overlay?.Close();
        }
    }
}
