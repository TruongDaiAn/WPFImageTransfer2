using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WPFPath = System.Windows.Shapes.Path;
using WPFPoint = System.Windows.Point;
using SystemDrawingRect = System.Drawing.Rectangle;

namespace WPFImageTransfer.Services.UI
{
    /// <summary>
    /// Điều khiển các vùng chọn (Rectangle, Ellipse, GrabCut) và lớp phủ mặt nạ động (Discord Mask Overlay).
    /// </summary>
    public class SelectionOverlayController
    {
        private readonly Canvas _selectionCanvas;
        private readonly Rectangle _selectionRectangle;
        private readonly Ellipse _selectionEllipse;
        private readonly WPFPath _discordMaskOverlay;
        private readonly Grid _maskOverlayContainer;
        private readonly Button _btnApplyMovableMask;
        private readonly Button _btnCancelMovableMask;
        private readonly Button _btnCropSelection;
        private readonly Button _btnCancelSelection;
        private readonly ScrollViewer _scrollViewer;

        public bool IsSelectionMode { get; private set; } = false;
        public bool IsDrawingSelection { get; private set; } = false;
        public bool IsEllipseSelection { get; private set; } = false;
        public bool IsObjectSelection { get; private set; } = false;
        public bool IsEditingMask { get; private set; } = false;
        public bool CurrentMaskIsCircle { get; private set; } = true;

        private WPFPoint _selectionStartPoint;

        public SelectionOverlayController(
            Canvas selectionCanvas,
            Rectangle selectionRectangle,
            Ellipse selectionEllipse,
            WPFPath discordMaskOverlay,
            Grid maskOverlayContainer,
            Button btnApplyMovableMask,
            Button btnCancelMovableMask,
            Button btnCropSelection,
            Button btnCancelSelection,
            ScrollViewer scrollViewer)
        {
            _selectionCanvas = selectionCanvas;
            _selectionRectangle = selectionRectangle;
            _selectionEllipse = selectionEllipse;
            _discordMaskOverlay = discordMaskOverlay;
            _maskOverlayContainer = maskOverlayContainer;
            _btnApplyMovableMask = btnApplyMovableMask;
            _btnCancelMovableMask = btnCancelMovableMask;
            _btnCropSelection = btnCropSelection;
            _btnCancelSelection = btnCancelSelection;
            _scrollViewer = scrollViewer;
        }

        public void StartMovableMask(bool isCircle)
        {
            CancelSelection();
            IsEditingMask = true;
            CurrentMaskIsCircle = isCircle;
            _discordMaskOverlay.Visibility = Visibility.Visible;
            UpdateDiscordMaskOverlay();

            _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

            _btnApplyMovableMask.Visibility = Visibility.Visible;
            _btnCancelMovableMask.Visibility = Visibility.Visible;
        }

        public void CancelMovableMask(TranslateTransform? imgTranslate = null)
        {
            IsEditingMask = false;
            _discordMaskOverlay.Visibility = Visibility.Collapsed;
            _btnApplyMovableMask.Visibility = Visibility.Collapsed;
            _btnCancelMovableMask.Visibility = Visibility.Collapsed;

            _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            if (imgTranslate != null)
            {
                imgTranslate.X = 0;
                imgTranslate.Y = 0;
            }
        }

        public void UpdateDiscordMaskOverlay()
        {
            if (_discordMaskOverlay.Visibility != Visibility.Visible) return;

            double w = _maskOverlayContainer.ActualWidth;
            double h = _maskOverlayContainer.ActualHeight;
            if (w <= 0 || h <= 0) return;

            RectangleGeometry outer = new RectangleGeometry(new Rect(0, 0, w, h));
            Geometry inner = CurrentMaskIsCircle
                ? new EllipseGeometry(new WPFPoint(w / 2, h / 2), 150, 150)
                : new RectangleGeometry(new Rect(w / 2 - 150, h / 2 - 150, 300, 300));

            _discordMaskOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        }

        public void StartSelection(bool isEllipse, bool isObject)
        {
            CancelMovableMask();
            IsSelectionMode = true;
            IsEllipseSelection = isEllipse;
            IsObjectSelection = isObject;

            _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _btnCropSelection.Visibility = Visibility.Visible;
            _btnCancelSelection.Visibility = Visibility.Visible;
        }

        public void CancelSelection()
        {
            IsSelectionMode = false;
            IsDrawingSelection = false;
            IsObjectSelection = false;

            _selectionRectangle.Visibility = Visibility.Collapsed;
            _selectionEllipse.Visibility = Visibility.Collapsed;
            _btnCropSelection.Visibility = Visibility.Collapsed;
            _btnCancelSelection.Visibility = Visibility.Collapsed;

            _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        public bool HandleMouseDown(MouseButtonEventArgs e)
        {
            if (!IsSelectionMode) return false;

            IsDrawingSelection = true;
            _selectionStartPoint = e.GetPosition(_selectionCanvas);

            if (IsEllipseSelection)
            {
                _selectionEllipse.Visibility = Visibility.Visible;
                Canvas.SetLeft(_selectionEllipse, _selectionStartPoint.X);
                Canvas.SetTop(_selectionEllipse, _selectionStartPoint.Y);
                _selectionEllipse.Width = 0;
                _selectionEllipse.Height = 0;
                _selectionRectangle.Visibility = Visibility.Collapsed;
            }
            else
            {
                _selectionRectangle.Visibility = Visibility.Visible;
                Canvas.SetLeft(_selectionRectangle, _selectionStartPoint.X);
                Canvas.SetTop(_selectionRectangle, _selectionStartPoint.Y);
                _selectionRectangle.Width = 0;
                _selectionRectangle.Height = 0;
                _selectionEllipse.Visibility = Visibility.Collapsed;
            }

            _scrollViewer.CaptureMouse();
            return true;
        }

        public bool HandleMouseMove(MouseEventArgs e)
        {
            if (!IsDrawingSelection) return false;

            WPFPoint currentPos = e.GetPosition(_selectionCanvas);
            double x = Math.Min(currentPos.X, _selectionStartPoint.X);
            double y = Math.Min(currentPos.Y, _selectionStartPoint.Y);
            double width = Math.Abs(currentPos.X - _selectionStartPoint.X);
            double height = Math.Abs(currentPos.Y - _selectionStartPoint.Y);

            if (IsEllipseSelection)
            {
                Canvas.SetLeft(_selectionEllipse, x);
                Canvas.SetTop(_selectionEllipse, y);
                _selectionEllipse.Width = width;
                _selectionEllipse.Height = height;
            }
            else
            {
                Canvas.SetLeft(_selectionRectangle, x);
                Canvas.SetTop(_selectionRectangle, y);
                _selectionRectangle.Width = width;
                _selectionRectangle.Height = height;
            }
            return true;
        }

        public bool HandleMouseUp()
        {
            if (!IsDrawingSelection) return false;

            IsDrawingSelection = false;
            _scrollViewer.ReleaseMouseCapture();
            return true;
        }

        public SystemDrawingRect? GetSafeCropRect(double pixelWidth, double pixelHeight)
        {
            double cropX = 0, cropY = 0, cropW = 0, cropH = 0;
            if (IsEllipseSelection)
            {
                if (_selectionEllipse.Visibility != Visibility.Visible || _selectionEllipse.Width <= 0) return null;
                cropX = Canvas.GetLeft(_selectionEllipse);
                cropY = Canvas.GetTop(_selectionEllipse);
                cropW = _selectionEllipse.Width;
                cropH = _selectionEllipse.Height;
            }
            else
            {
                if (_selectionRectangle.Visibility != Visibility.Visible || _selectionRectangle.Width <= 0) return null;
                cropX = Canvas.GetLeft(_selectionRectangle);
                cropY = Canvas.GetTop(_selectionRectangle);
                cropW = _selectionRectangle.Width;
                cropH = _selectionRectangle.Height;
            }

            double scaleX = pixelWidth / _selectionCanvas.ActualWidth;
            double scaleY = pixelHeight / _selectionCanvas.ActualHeight;

            int realX = (int)(cropX * scaleX);
            int realY = (int)(cropY * scaleY);
            int realW = (int)(cropW * scaleX);
            int realH = (int)(cropH * scaleY);

            return new SystemDrawingRect(realX, realY, realW, realH);
        }
    }
}
