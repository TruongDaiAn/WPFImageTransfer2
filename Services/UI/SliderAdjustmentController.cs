using Emgu.CV;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Services.Optimization;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Services.UI
{
    /// <summary>
    /// Quản lý việc kéo trượt điều chỉnh Độ sáng, Tương phản, Độ mờ đục với cơ chế Non-blocking Pipeline siêu tốc 60+ FPS và Fast WriteableBitmap Bridge.
    /// </summary>
    public class SliderAdjustmentController
    {
        private readonly Slider _sliderBrightness;
        private readonly Slider _sliderContrast;
        private readonly Slider _sliderOpacity;
        private readonly TextBox _txtBrightness;
        private readonly TextBox _txtContrast;
        private readonly TextBox _txtOpacity;
        private readonly Image _imgPreview;
        private readonly ImageCacheManager _cache;
        private readonly FastWriteableBitmapBridge _writeableBridge = new FastWriteableBitmapBridge();

        private double _requestedBrightness = 0;
        private double _requestedContrast = 1.0;
        private double _requestedOpacity = 100;
        private volatile bool _hasPendingUpdate = false;
        private bool _isProcessingLoopActive = false;

        public Action? OnStateSaveRequested { get; set; }

        public SliderAdjustmentController(
            Slider sliderBrightness,
            Slider sliderContrast,
            Slider sliderOpacity,
            TextBox txtBrightness,
            TextBox txtContrast,
            TextBox txtOpacity,
            Image imgPreview)
        {
            _sliderBrightness = sliderBrightness;
            _sliderContrast = sliderContrast;
            _sliderOpacity = sliderOpacity;
            _txtBrightness = txtBrightness;
            _txtContrast = txtContrast;
            _txtOpacity = txtOpacity;
            _imgPreview = imgPreview;
            _cache = ImageCacheManager.Instance;
        }

        public void ResetSliders()
        {
            _hasPendingUpdate = false;
            _sliderBrightness.Value = 0;
            _txtBrightness.Text = "0";
            _sliderContrast.Value = 1.0;
            _txtContrast.Text = "1.0";
            _sliderOpacity.Value = 100;
            _txtOpacity.Text = "100";
            _requestedBrightness = 0;
            _requestedContrast = 1.0;
            _requestedOpacity = 100;
            _writeableBridge.Reset();
        }

        public void HandleDragStarted(string currentPath)
        {
            if (_cache.OriginalImageForSliders == null && _imgPreview.Source is BitmapSource src)
            {
                _cache.SetSliderBaseline(currentPath, src);
                OnStateSaveRequested?.Invoke();
            }
        }

        public void HandleDragCompleted(string currentPath)
        {
            if (_cache.OriginalImageForSliders != null && _imgPreview.Source is BitmapSource current)
            {
                if (!string.IsNullOrEmpty(currentPath))
                {
                    _cache.UpdateImage(currentPath, current, _cache.LatestAdjustedMat);
                }
                _cache.ClearSliderBaseline();
            }
        }

        public void HandleValueChanged(string currentPath)
        {
            _txtBrightness.Text = _sliderBrightness.Value.ToString("0");
            _txtContrast.Text = _sliderContrast.Value.ToString("0.0");
            _txtOpacity.Text = _sliderOpacity.Value.ToString("0");

            if (_cache.OriginalImageForSliders == null && _imgPreview.Source is BitmapSource src)
            {
                _cache.SetSliderBaseline(currentPath, src);
            }

            _requestedBrightness = _sliderBrightness.Value;
            _requestedContrast = _sliderContrast.Value;
            _requestedOpacity = _sliderOpacity.Value;
            _hasPendingUpdate = true;

            if (!_isProcessingLoopActive)
            {
                _ = ProcessLoopAsync();
            }
        }

        public async Task HandleTextSubmitAsync(string currentPath)
        {
            OnStateSaveRequested?.Invoke();
            if (_cache.OriginalImageForSliders == null && _imgPreview.Source is BitmapSource src)
            {
                _cache.SetSliderBaseline(currentPath, src);
            }
            ApplyTextValuesToSliders();
            await ApplySlidersAsync();

            if (!string.IsNullOrEmpty(currentPath) && _imgPreview.Source is BitmapSource resSrc)
            {
                _cache.UpdateImage(currentPath, resSrc, _cache.LatestAdjustedMat);
            }
            _cache.ClearSliderBaseline();
        }

        public void ApplyTextValuesToSliders()
        {
            if (double.TryParse(_txtBrightness.Text, out double b))
                _sliderBrightness.Value = Math.Max(_sliderBrightness.Minimum, Math.Min(_sliderBrightness.Maximum, b));
            if (double.TryParse(_txtContrast.Text, out double c))
                _sliderContrast.Value = Math.Max(_sliderContrast.Minimum, Math.Min(_sliderContrast.Maximum, c));
            if (double.TryParse(_txtOpacity.Text, out double o))
                _sliderOpacity.Value = Math.Max(_sliderOpacity.Minimum, Math.Min(_sliderOpacity.Maximum, o));
        }

        public async Task ApplySlidersAsync()
        {
            _requestedBrightness = _sliderBrightness.Value;
            _requestedContrast = _sliderContrast.Value;
            _requestedOpacity = _sliderOpacity.Value;
            _hasPendingUpdate = true;

            await ProcessLoopAsync();
        }

        private async Task ProcessLoopAsync()
        {
            if (_isProcessingLoopActive) return;
            _isProcessingLoopActive = true;

            try
            {
                while (_hasPendingUpdate)
                {
                    Mat? baseMat = _cache.OriginalMatForSliders;
                    if (baseMat == null || baseMat.IsEmpty) break;

                    _hasPendingUpdate = false;
                    double brightness = _requestedBrightness;
                    double contrast = _requestedContrast;
                    double opacity = _requestedOpacity;

                    Mat adjustedMat = await Task.Run(() =>
                    {
                        return ImageFilterService.ApplySliders(baseMat, brightness, contrast, opacity);
                    });

                    if (adjustedMat != null && !adjustedMat.IsEmpty)
                    {
                        var writeableSource = _writeableBridge.UpdateFromMat(adjustedMat);
                        if (_imgPreview.Source != writeableSource)
                        {
                            _imgPreview.Source = writeableSource;
                        }

                        if (_cache.LatestAdjustedMat != null) _cache.LatestAdjustedMat.Dispose();
                        _cache.LatestAdjustedMat = adjustedMat;
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi trong quá trình render nhanh
            }
            finally
            {
                _isProcessingLoopActive = false;
                if (_hasPendingUpdate)
                {
                    _ = ProcessLoopAsync();
                }
            }
        }
    }
}
