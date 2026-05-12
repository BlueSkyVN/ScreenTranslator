using System;
using System.Windows;
using System.Windows.Input;

namespace ScreenTranslator.UI
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
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
    }
}
