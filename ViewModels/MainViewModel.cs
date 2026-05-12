using System;
using System.Drawing;
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

        public MainViewModel()
        {
            _captureService = new ScreenCaptureService();
            _ocrService = new OcrService("en-US");
            _translationService = new TranslationService();

            SelectRegionCommand = new RelayCommand(_ => SelectRegion());
            StartTranslationCommand = new RelayCommand(_ => ToggleTranslation());

            // Default capture region (bottom 30% of screen)
            int screenW = (int)SystemParameters.PrimaryScreenWidth;
            int screenH = (int)SystemParameters.PrimaryScreenHeight;
            int captureHeight = (int)(screenH * 0.3);
            _captureRegion = new Rectangle(0, screenH - captureHeight, screenW, captureHeight);

            // Open overlay by default
            _overlay = new OverlayWindow();
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
                }
            }
        }

        private string _apiKey = "";
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        private string _apiModel = "groq|llama-3.1-8b-instant";
        public string ApiModel
        {
            get => _apiModel;
            set => SetProperty(ref _apiModel, value);
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

        public ICommand SelectRegionCommand { get; }
        public ICommand StartTranslationCommand { get; }

        private void SelectRegion()
        {
            var regionWindow = new RegionSelectionWindow();
            if (regionWindow.ShowDialog() == true && !regionWindow.SelectedRegion.IsEmpty)
            {
                _captureRegion = regionWindow.SelectedRegion;
                MessageBox.Show($"Region updated:\nLocation: ({_captureRegion.X}, {_captureRegion.Y})\nSize: {_captureRegion.Width}x{_captureRegion.Height}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ToggleTranslation()
        {
            if (IsRunning)
            {
                // Stop
                IsRunning = false;
                _cts?.Cancel();
                _overlay?.UpdateText("");
            }
            else
            {
                // Start
                if (string.IsNullOrWhiteSpace(ApiKey))
                {
                    MessageBox.Show("Vui lòng nhập API Key trước khi bắt đầu!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // Cannot unset the checkbox easily without full binding on IsChecked, but let's assume IsRunning controls it.
                    IsRunning = false;
                    return;
                }

                IsRunning = true;
                _cts = new CancellationTokenSource();
                Task.Run(() => TranslationLoop(_cts.Token));
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

                        // Ensure we update UI properties on main thread if needed, but since it's WPF, we should dispatch.
                        // However, PropertyChanged on ViewModel often works if dispatched, but let's dispatch safely.
                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            LastOcrText = string.IsNullOrWhiteSpace(text) ? "(Không tìm thấy chữ nào trong vùng này)" : text;
                        });

                        if (!string.IsNullOrWhiteSpace(text) && text != lastText)
                        {
                            var translated = await _translationService.TranslateAsync(text, "en", TargetLang, ApiKey, apiType, modelName);
                            _overlay?.UpdateText(translated);
                            lastText = text;
                        }
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() => LastOcrText = "Capture failed (returned null).");
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => LastOcrText = "Lỗi: " + ex.Message);
                }

                await Task.Delay(1000, token);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _overlay?.Close();
        }
    }
}
