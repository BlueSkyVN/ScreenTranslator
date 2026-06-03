using System;
using System.Windows;
using System.Windows.Input;

namespace ScreenTranslator.UI
{
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private bool _isClickThroughPending = false;

        public OverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Apply click through style on startup if it was loaded from settings
            SetClickThrough(_isClickThroughPending);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !_isClickThroughPending)
            {
                this.DragMove();
            }
        }

        public void UpdateText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    TranslatedText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TranslatedText.Visibility = Visibility.Visible;
                    TranslatedText.Text = text;
                }
            });
        }

        public void SetOpacity(double opacity)
        {
            Dispatcher.Invoke(() =>
            {
                if (OverlayBorder == null) return;
                byte alpha = (byte)(opacity * 255);
                OverlayBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 17, 17, 17));
            });
        }

        public void SetFontSize(double size)
        {
            Dispatcher.Invoke(() =>
            {
                if (TranslatedText == null) return;
                TranslatedText.FontSize = size;
            });
        }

        public void SetTextColor(string colorHex)
        {
            Dispatcher.Invoke(() =>
            {
                if (TranslatedText == null) return;
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                    TranslatedText.Foreground = new System.Windows.Media.SolidColorBrush(color);
                }
                catch { }
            });
        }

        public void SetClickThrough(bool clickThrough)
        {
            _isClickThroughPending = clickThrough;
            Dispatcher.Invoke(() =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (clickThrough)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                    this.Cursor = Cursors.Arrow;
                }
                else
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
                    this.Cursor = Cursors.Hand;
                }
            });
        }
    }
}
