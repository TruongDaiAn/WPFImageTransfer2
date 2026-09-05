using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WPFPoint = System.Windows.Point;

namespace WPFImageTransfer.Services.UI
{
    /// <summary>
    /// Quản lý thao tác phóng to, thu nhỏ (Zoom) và kéo rê ảnh (Pan) trên ScrollViewer.
    /// </summary>
    public class PanZoomController
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly ScaleTransform _imgScale;
        private readonly TranslateTransform _imgTranslate;
        private WPFPoint _lastMousePosition;
        private bool _isDragging = false;

        public Func<bool>? IsEditingMaskPredicate { get; set; }

        public PanZoomController(ScrollViewer scrollViewer, ScaleTransform imgScale, TranslateTransform imgTranslate)
        {
            _scrollViewer = scrollViewer;
            _imgScale = imgScale;
            _imgTranslate = imgTranslate;
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            double zoomStep = 1.1;
            if (e.Delta > 0)
            {
                _imgScale.ScaleX *= zoomStep;
                _imgScale.ScaleY *= zoomStep;
            }
            else
            {
                if (_imgScale.ScaleX > 0.1)
                {
                    _imgScale.ScaleX /= zoomStep;
                    _imgScale.ScaleY /= zoomStep;
                }
            }
            e.Handled = true;
        }

        public void HandleMouseDown(MouseButtonEventArgs e)
        {
            bool isEditingMask = IsEditingMaskPredicate?.Invoke() ?? false;
            if (_imgScale.ScaleX > 1 || isEditingMask)
            {
                _lastMousePosition = e.GetPosition(_scrollViewer);
                _isDragging = true;
                _scrollViewer.Cursor = Cursors.Hand;
                _scrollViewer.CaptureMouse();
            }
        }

        public void HandleMouseMove(MouseEventArgs e)
        {
            if (!_isDragging) return;

            WPFPoint currentPosition = e.GetPosition(_scrollViewer);
            double deltaX = currentPosition.X - _lastMousePosition.X;
            double deltaY = currentPosition.Y - _lastMousePosition.Y;

            bool isEditingMask = IsEditingMaskPredicate?.Invoke() ?? false;
            if (isEditingMask)
            {
                _imgTranslate.X += deltaX / _imgScale.ScaleX;
                _imgTranslate.Y += deltaY / _imgScale.ScaleY;
            }
            else
            {
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - deltaX);
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - deltaY);
            }

            _lastMousePosition = currentPosition;
        }

        public void HandleMouseUp()
        {
            _isDragging = false;
            _scrollViewer.Cursor = Cursors.Arrow;
            _scrollViewer.ReleaseMouseCapture();
        }

        public void Reset()
        {
            _imgScale.ScaleX = 1;
            _imgScale.ScaleY = 1;
            _imgTranslate.X = 0;
            _imgTranslate.Y = 0;
        }
    }
}
