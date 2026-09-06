using Emgu.CV;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WPFImageTransfer.Controllers;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services.Optimization;
using WPFImageTransfer.Services.Processing;
using Point = System.Windows.Point;

namespace WPFImageTransfer
{
    /// <summary>
    /// Cửa sổ điều khiển và tinh chỉnh tham số tăng cường ảnh thiếu sáng (GHE, AHE, CLAHE)
    /// Tích hợp 3 chế độ so sánh trước/sau: Chia đôi (Split View), Song song (Dual View), Đơn lẻ (Single View)
    /// cùng biểu đồ phân bố Histogram thời gian thực.
    /// </summary>
    public partial class LowLightEnhanceWindow : Window
    {
        private readonly Mat _originalMat;
        private readonly BitmapSource _originalBitmapSource;
        private Mat? _currentEnhancedMat;
        private Mat? _referenceMat;
        private readonly int[] _originalHist;
        private readonly FastWriteableBitmapBridge _writeableBridge = new FastWriteableBitmapBridge();
        private readonly LowLightEnhancementController _enhancementController = new LowLightEnhancementController();

        private bool _isProcessing = false;
        private volatile bool _hasPendingUpdate = false;
        private bool _isDraggingSplit = false;

        public Mat? ResultMat { get; private set; }
        public bool IsApplied { get; private set; } = false;

        public LowLightEnhanceWindow(Mat originalMat)
        {
            InitializeComponent();
            _originalMat = originalMat.Clone();
            _originalHist = HistogramEnhancementService.CalculateLuminanceHistogram(_originalMat);

            var origBmp = ImageConversionHelper.ToBitmapSource(_originalMat);
            origBmp?.Freeze();
            _originalBitmapSource = origBmp!;

            // Gán ảnh gốc ban đầu cho các view
            imgSplitOriginal.Source = _originalBitmapSource;
            imgSideOriginal.Source = _originalBitmapSource;

            Loaded += (s, e) =>
            {
                UpdateSplitLayout();
                _ = TriggerUpdateAsync();
            };
        }

        #region Algorithm & Parameters Event Handlers

        private void Algorithm_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            if (panelClipLimit != null)
                panelClipLimit.Visibility = rbCLAHE.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (panelGridSize != null)
                panelGridSize.Visibility = rbGHE.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

            _ = TriggerUpdateAsync();
        }

        private void SliderClipLimit_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (lblClipLimitVal != null)
            {
                lblClipLimitVal.Text = sliderClipLimit.Value.ToString("0.0");
            }
            _ = TriggerUpdateAsync();
        }

        private void CmbGridSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _ = TriggerUpdateAsync();
        }

        private void CmbColorSpace_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _ = TriggerUpdateAsync();
        }

        #endregion

        #region Comparison View Modes Handlers

        private void ViewMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || viewSplit == null || viewSideBySide == null || viewSingle == null) return;

            bool isSplit = rbModeSplit.IsChecked == true;
            bool isSideBySide = rbModeSideBySide.IsChecked == true;
            bool isSingle = rbModeSingle.IsChecked == true;

            viewSplit.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
            viewSideBySide.Visibility = isSideBySide ? Visibility.Visible : Visibility.Collapsed;
            viewSingle.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;

            if (panelSplitSlider != null)
                panelSplitSlider.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;

            if (cbShowOriginal != null)
                cbShowOriginal.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;

            if (isSplit)
            {
                UpdateSplitLayout();
            }
        }

        private void SliderSplitRatio_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (lblSplitPercent != null)
            {
                lblSplitPercent.Text = $"{(int)sliderSplitRatio.Value}%";
            }
            UpdateSplitLayout();
        }

        private void SplitView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingSplit = true;
                viewSplit.CaptureMouse();
                UpdateSplitFromPoint(e.GetPosition(viewSplit));
            }
        }

        private void SplitView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSplit)
            {
                UpdateSplitFromPoint(e.GetPosition(viewSplit));
            }
        }

        private void SplitView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSplit)
            {
                _isDraggingSplit = false;
                viewSplit.ReleaseMouseCapture();
            }
        }

        private void UpdateSplitFromPoint(Point pos)
        {
            if (viewSplit.ActualWidth <= 0) return;
            double ratio = Math.Clamp(pos.X / viewSplit.ActualWidth, 0.0, 1.0);
            sliderSplitRatio.Value = ratio * 100.0;
        }

        private void UpdateSplitLayout()
        {
            if (viewSplit == null || imgSplitOriginal == null || lineDivider == null || thumbDivider == null) return;

            double width = viewSplit.ActualWidth;
            double height = viewSplit.ActualHeight;

            if (width <= 0 || height <= 0) return;

            double ratio = Math.Clamp(sliderSplitRatio.Value / 100.0, 0.0, 1.0);
            double splitX = width * ratio;

            // Cắt tỉa ảnh gốc theo chiều dọc từ 0 đến splitX
            imgSplitOriginal.Clip = new RectangleGeometry(new Rect(0, 0, splitX, height));

            // Cập nhật vị trí đường kẻ phân cách
            lineDivider.X1 = splitX;
            lineDivider.X2 = splitX;
            lineDivider.Y1 = 0;
            lineDivider.Y2 = height;

            // Cập nhật vị trí nút kéo tròn
            Canvas.SetLeft(thumbDivider, splitX - (thumbDivider.Width / 2.0));
            Canvas.SetTop(thumbDivider, (height / 2.0) - (thumbDivider.Height / 2.0));
        }

        private void MainDisplayContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (rbModeSplit.IsChecked == true)
            {
                UpdateSplitLayout();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (rbModeSplit.IsChecked == true)
            {
                UpdateSplitLayout();
            }
        }

        #endregion

        #region Core Processing Loop

        private async Task TriggerUpdateAsync()
        {
            _hasPendingUpdate = true;
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                while (_hasPendingUpdate)
                {
                    _hasPendingUpdate = false;

                    double clipLimit = sliderClipLimit.Value;
                    int gridSize = 8;
                    if (cmbGridSize.SelectedItem is ComboBoxItem itemGrid && int.TryParse(itemGrid.Tag?.ToString(), out int parsedGrid))
                    {
                        gridSize = parsedGrid;
                    }

                    var colorSpace = HistogramEnhancementService.EnhancementColorSpace.YCrCb;
                    if (cmbColorSpace.SelectedItem is ComboBoxItem itemColor)
                    {
                        string tag = itemColor.Tag?.ToString() ?? "";
                        if (tag == "Lab") colorSpace = HistogramEnhancementService.EnhancementColorSpace.Lab;
                        else if (tag == "Grayscale") colorSpace = HistogramEnhancementService.EnhancementColorSpace.Grayscale;
                    }

                    string algorithm = rbGHE.IsChecked == true ? "GHE" : rbAHE.IsChecked == true ? "AHE" : "CLAHE";
                    LowLightEnhancementRequest request = new(_originalMat, algorithm, clipLimit, gridSize, colorSpace, _referenceMat);
                    LowLightEnhancementResult result = await _enhancementController.ProcessAsync(request);
                    Mat processedMat = result.Enhanced;

                    if (_currentEnhancedMat != null)
                    {
                        _currentEnhancedMat.Dispose();
                    }
                    _currentEnhancedMat = processedMat;

                    // Cập nhật hiển thị lên các chế độ xem
                    var writeableBmp = _writeableBridge.UpdateFromMat(_currentEnhancedMat);

                    imgSplitEnhanced.Source = writeableBmp;
                    imgSideEnhanced.Source = writeableBmp;
                    imgSingleEnhanced.Source = writeableBmp;

                    UpdateSplitLayout();

                    string algoName = algorithm == "GHE" ? "GHE (Toàn cục)" : algorithm == "AHE" ? $"AHE (Lưới {gridSize}x{gridSize})" : $"CLAHE (Lưới {gridSize}x{gridSize}, Clip {clipLimit:0.0})";
                    lblPerformanceInfo.Text = $"Độ phân giải: {_originalMat.Width}x{_originalMat.Height} | Thời gian: {result.ProcessingTimeMilliseconds}ms | Thuật toán: {algoName} [{colorSpace}]";

                    // Trích xuất và vẽ biểu đồ Histogram
                    DrawHistogramCanvas(_originalHist, result.EnhancedHistogram);
                    UpdateQualityEvaluation(result.Quality);
                }
            }
            finally
            {
                _isProcessing = false;
                if (_hasPendingUpdate)
                {
                    _ = TriggerUpdateAsync();
                }
            }
        }

        #endregion

        #region Histogram Visualizer

        private void DrawHistogramCanvas(int[] origHist, int[] enhHist)
        {
            cvHistogram.Children.Clear();

            double width = cvHistogram.ActualWidth > 0 ? cvHistogram.ActualWidth : 320;
            double height = cvHistogram.ActualHeight > 0 ? cvHistogram.ActualHeight : 125;

            int maxOrig = 1;
            int maxEnh = 1;
            for (int i = 0; i < 256; i++)
            {
                if (origHist[i] > maxOrig) maxOrig = origHist[i];
                if (enhHist[i] > maxEnh) maxEnh = enhHist[i];
            }
            int maxVal = Math.Max(maxOrig, maxEnh);

            // Vẽ lưới trục tọa độ
            for (int step = 1; step <= 3; step++)
            {
                double yLine = height * (step / 4.0);
                Line gridLine = new Line
                {
                    X1 = 0,
                    Y1 = yLine,
                    X2 = width,
                    Y2 = yLine,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                cvHistogram.Children.Add(gridLine);
            }

            // Vẽ biểu đồ ảnh gốc (Đỏ)
            System.Windows.Media.PointCollection origPoints = new System.Windows.Media.PointCollection();
            origPoints.Add(new Point(0, height));
            for (int i = 0; i < 256; i++)
            {
                double x = (double)i / 255.0 * width;
                double h = ((double)origHist[i] / maxVal) * (height - 5);
                origPoints.Add(new Point(x, height - h));
            }
            origPoints.Add(new Point(width, height));

            Polygon origPoly = new Polygon
            {
                Points = origPoints,
                Fill = new SolidColorBrush(Color.FromArgb(50, 232, 17, 35)),
                Stroke = new SolidColorBrush(Color.FromRgb(232, 17, 35)),
                StrokeThickness = 1.0
            };
            cvHistogram.Children.Add(origPoly);

            // Vẽ biểu đồ ảnh sau xử lý (Xanh Cyan)
            System.Windows.Media.PointCollection enhPoints = new System.Windows.Media.PointCollection();
            enhPoints.Add(new Point(0, height));
            for (int i = 0; i < 256; i++)
            {
                double x = (double)i / 255.0 * width;
                double h = ((double)enhHist[i] / maxVal) * (height - 5);
                enhPoints.Add(new Point(x, height - h));
            }
            enhPoints.Add(new Point(width, height));

            Polygon enhPoly = new Polygon
            {
                Points = enhPoints,
                Fill = new SolidColorBrush(Color.FromArgb(60, 0, 208, 255)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 208, 255)),
                StrokeThickness = 1.5
            };
            cvHistogram.Children.Add(enhPoly);
        }

        #endregion

        #region Actions & Navigation

        private void BtnSelectReference_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All files|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            using Mat loaded = CvInvoke.Imread(dialog.FileName, Emgu.CV.CvEnum.ImreadModes.AnyColor);
            if (loaded.IsEmpty)
            {
                MessageBox.Show("Không thể đọc ảnh tham chiếu.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _referenceMat?.Dispose();
            _referenceMat = loaded.Clone();
            lblReferenceInfo.Text = $"Đã thêm ảnh gốc để so sánh: {System.IO.Path.GetFileName(dialog.FileName)}";
            _ = TriggerUpdateAsync();
        }

        private void UpdateQualityEvaluation(ImageQualityEvaluation evaluation)
        {
            lblEntropy.Text = evaluation.Entropy.Value;
            lblEntropyNote.Text = evaluation.Entropy.Note;
            lblLoe.Text = evaluation.Loe.Value;
            lblPsnr.Text = evaluation.Psnr.Value;
            lblPsnrNote.Text = evaluation.Psnr.Note;
            lblSsim.Text = evaluation.Ssim.Value;
            lblSsimNote.Text = evaluation.Ssim.Note;
            lblNiqe.Text = evaluation.Niqe.Value;
            lblNiqeNote.Text = evaluation.Niqe.Note;
            lblBrisque.Text = evaluation.Brisque.Value;
            lblBrisqueNote.Text = evaluation.Brisque.Note;
        }

        private void CbShowOriginal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            imgSingleEnhanced.Source = _originalBitmapSource;
        }

        private void CbShowOriginal_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentEnhancedMat != null)
            {
                imgSingleEnhanced.Source = _writeableBridge.UpdateFromMat(_currentEnhancedMat);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            rbCLAHE.IsChecked = true;
            sliderClipLimit.Value = 2.5;
            cmbGridSize.SelectedIndex = 1; // 8x8
            cmbColorSpace.SelectedIndex = 0; // YCrCb
            sliderSplitRatio.Value = 50;
            _ = TriggerUpdateAsync();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsApplied = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEnhancedMat != null && !_currentEnhancedMat.IsEmpty)
            {
                ResultMat = _currentEnhancedMat.Clone();
                IsApplied = true;
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _originalMat.Dispose();
            _currentEnhancedMat?.Dispose();
            _referenceMat?.Dispose();
        }

        #endregion
    }
}
