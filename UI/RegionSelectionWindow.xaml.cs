using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace ScreenTranslator.UI
{
    public partial class RegionSelectionWindow : Window
    {
        private Point _startPoint;
        private bool _isDragging = false;
        public System.Drawing.Rectangle SelectedRegion { get; private set; }
        
        public RegionSelectionWindow()
        {
            InitializeComponent();
            
            // Cover all screens/monitors natively
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
            
            SelectedRegion = System.Drawing.Rectangle.Empty;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(this);
                SelectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionRect, _startPoint.X);
                Canvas.SetTop(SelectionRect, _startPoint.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var pos = e.GetPosition(this);
                var x = Math.Min(pos.X, _startPoint.X);
                var y = Math.Min(pos.Y, _startPoint.Y);
                var w = Math.Abs(pos.X - _startPoint.X);
                var h = Math.Abs(pos.Y - _startPoint.Y);

                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = w;
                SelectionRect.Height = h;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                
                var x = Canvas.GetLeft(SelectionRect);
                var y = Canvas.GetTop(SelectionRect);
                var w = SelectionRect.Width;
                var h = SelectionRect.Height;

                // Ensure the drawn rectangle is reasonably sized
                if (w > 10 && h > 10)
                {
                    double dpiX = 1.0;
                    double dpiY = 1.0;
                    var source = PresentationSource.FromVisual(this);
                    if (source != null)
                    {
                        dpiX = source.CompositionTarget.TransformToDevice.M11;
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    }

                    // Convert WPF virtual screen coordinates back to physical pixel coordinates
                    int sysX = (int)((this.Left + x) * dpiX);
                    int sysY = (int)((this.Top + y) * dpiY);
                    int physW = (int)(w * dpiX);
                    int physH = (int)(h * dpiY);

                    SelectedRegion = new System.Drawing.Rectangle(sysX, sysY, physW, physH);
                }
                
                this.DialogResult = true;
                this.Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
            }
        }
    }
}
