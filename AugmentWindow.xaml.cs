using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Controllers;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer
{
    public partial class AugmentWindow : Window
    {
        public BitmapSource? ResultSource { get; private set; }
        public List<BitmapSource> ResultSources { get; private set; } = new List<BitmapSource>();
        public List<Mat> ResultMats { get; private set; } = new List<Mat>();

        private readonly BitmapSource _originalSource;
        private Mat? _originalMat;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private bool _isUpdating = false;
        private readonly AugmentationController _augmentationController = new AugmentationController();

        public AugmentWindow(BitmapSource source)
        {
            InitializeComponent();
            _originalSource = source;
            ImgPreview.Source = _originalSource;

            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timer.Tick += Timer_Tick;

            _originalMat = ImageConversionHelper.BitmapSourceToMat(_originalSource);
            this.Closed += AugmentWindow_Closed;
        }

        private void AugmentWindow_Closed(object? sender, EventArgs e)
        {
            if (_originalMat != null)
            {
                _originalMat.Dispose();
                _originalMat = null;
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtRotate != null && sliderRotate != null) txtRotate.Text = sliderRotate.Value.ToString("0");
            if (txtShear != null && sliderShear != null) txtShear.Text = sliderShear.Value.ToString("0");
            if (txtShiftX != null && sliderShiftX != null) txtShiftX.Text = sliderShiftX.Value.ToString("0");
            if (txtShiftY != null && sliderShiftY != null) txtShiftY.Text = sliderShiftY.Value.ToString("0");
            TriggerUpdate();
        }

        private void TxtValues_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyTextValues();
        }

        private void TxtValues_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyTextValues();
        }

        private void ApplyTextValues()
        {
            if (double.TryParse(txtRotate.Text, out double r)) sliderRotate.Value = Math.Max(sliderRotate.Minimum, Math.Min(sliderRotate.Maximum, r));
            if (double.TryParse(txtShear.Text, out double sh)) sliderShear.Value = Math.Max(sliderShear.Minimum, Math.Min(sliderShear.Maximum, sh));
            if (double.TryParse(txtShiftX.Text, out double sx)) sliderShiftX.Value = Math.Max(sliderShiftX.Minimum, Math.Min(sliderShiftX.Maximum, sx));
            if (double.TryParse(txtShiftY.Text, out double sy)) sliderShiftY.Value = Math.Max(sliderShiftY.Minimum, Math.Min(sliderShiftY.Maximum, sy));
            TriggerUpdate();
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            TriggerUpdate();
        }

        private void TriggerUpdate()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            ApplyAugmentation();
        }

        private async void ApplyAugmentation()
        {
            if (_originalSource == null || _originalMat == null || _isUpdating) return;
            _isUpdating = true;
            txtStatus.Visibility = Visibility.Visible;

            var param = new AugmentationParameters
            {
                FlipH = chkFlipH.IsChecked == true,
                FlipV = chkFlipV.IsChecked == true,
                RotateAngle = sliderRotate.Value,
                ShearAngle = sliderShear.Value,
                ShiftXPercent = sliderShiftX.Value,
                ShiftYPercent = sliderShiftY.Value
            };

            try
            {
                BitmapSource? result = await _augmentationController.PreviewAsync(_originalMat, param);

                if (result != null)
                {
                    ImgPreview.Source = result;
                }
            }
            catch
            {
                // Bỏ qua lỗi tạm thời khi kéo trượt liên tục
            }
            finally
            {
                txtStatus.Visibility = Visibility.Collapsed;
                _isUpdating = false;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            chkFlipH.IsChecked = false;
            chkFlipV.IsChecked = false;
            sliderRotate.Value = 0;
            sliderShear.Value = 0;
            sliderShiftX.Value = 0;
            sliderShiftY.Value = 0;
            if (sliderCount != null) sliderCount.Value = 1;
            ApplyTextValues();
            ApplyCountTextValue();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_originalMat == null) return;

            int count = sliderCount != null ? (int)sliderCount.Value : 1;
            if (count <= 1)
            {
                ResultSource = ImgPreview.Source as BitmapSource;
                if (ResultSource != null)
                {
                    ResultSources = new List<BitmapSource> { ResultSource };
                    ResultMats = new List<Mat> { ImageConversionHelper.BitmapSourceToMat(ResultSource) };
                }
                this.DialogResult = true;
                this.Close();
                return;
            }

            txtStatus.Text = "Đang sinh bộ ảnh...";
            txtStatus.Visibility = Visibility.Visible;
            this.IsEnabled = false;

            var param = new AugmentationParameters
            {
                FlipH = chkFlipH.IsChecked == true,
                FlipV = chkFlipV.IsChecked == true,
                RotateAngle = sliderRotate.Value,
                ShearAngle = sliderShear.Value,
                ShiftXPercent = sliderShiftX.Value,
                ShiftYPercent = sliderShiftY.Value
            };

            try
            {
                var result = await _augmentationController.GenerateBatchAsync(_originalMat, count, param);

                ResultSources = result.Sources;
                ResultMats = result.Mats;
                ResultSource = ResultSources.Count > 0 ? ResultSources[0] : null;

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi sinh bộ ảnh augmentation: " + ex.Message);
            }
            finally
            {
                txtStatus.Visibility = Visibility.Collapsed;
                this.IsEnabled = true;
            }
        }

        private void SliderCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtCount != null && sliderCount != null) txtCount.Text = sliderCount.Value.ToString("0");
        }

        private void TxtCount_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyCountTextValue();
        }

        private void TxtCount_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyCountTextValue();
        }

        private void ApplyCountTextValue()
        {
            if (int.TryParse(txtCount.Text, out int c))
            {
                sliderCount.Value = Math.Max(sliderCount.Minimum, Math.Min(sliderCount.Maximum, c));
            }
        }
    }
}
