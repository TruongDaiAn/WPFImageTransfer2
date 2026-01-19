using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WPFImageTransfer
{
    public partial class PreviewWindow : Window
    {
        private Point _lastMousePosition;
        private bool _isDragging = false;

        public PreviewWindow(ImageSource imageSource)
        {
            InitializeComponent();
            imgFull.Source = imageSource; // Nhận ảnh được truyền từ MainWindow
        }

        private void Image_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomStep = 1.1;
            if (e.Delta > 0) { imgScale.ScaleX *= zoomStep; imgScale.ScaleY *= zoomStep; }
            else { if (imgScale.ScaleX > 0.1) { imgScale.ScaleX /= zoomStep; imgScale.ScaleY /= zoomStep; } }
            e.Handled = true;
        }

        private void scrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (imgScale.ScaleX > 1) { _lastMousePosition = e.GetPosition(scrollViewer); _isDragging = true; scrollViewer.CaptureMouse(); }
        }

        private void scrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(scrollViewer);
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - (currentPosition.X - _lastMousePosition.X));
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (currentPosition.Y - _lastMousePosition.Y));
                _lastMousePosition = currentPosition;
            }
        }

        private void scrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            scrollViewer.ReleaseMouseCapture();
        }
    }
}