using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Controllers;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services;
using WPFImageTransfer.Services.Processing;
using WPFImageTransfer.Services.UI;
using WPFPoint = System.Windows.Point;

namespace WPFImageTransfer
{
    /// <summary>
    /// Cửa sổ chính của ứng dụng WPFImageTransfer.
    /// Đóng vai trò là View & Coordinator kết nối các Service xử lý ảnh, cache và UI Controller.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Services & Controllers

        private readonly ImageWorkspaceModel _workspace = new ImageWorkspaceModel();
        private readonly ImageCacheManager _cache;
        private readonly ImageHistoryManager _history;
        private readonly MainWindowController _mainController;
        private readonly ImageEditingController _editingController;
        private readonly FileController _fileController = new FileController();
        private readonly PreviewController _previewController = new PreviewController();

        private readonly PanZoomController _panZoomController;
        private readonly SelectionOverlayController _selectionController;
        private readonly SliderAdjustmentController _sliderController;

        public ObservableCollection<string> ImageList => _cache.ImageList;
        public Dictionary<string, BitmapSource> PendingImages => _cache.PendingImages;

        #endregion

        public MainWindow()
        {
            InitializeComponent();
            _cache = _workspace.Cache;
            _history = _workspace.History;
            _mainController = new MainWindowController(_workspace);
            _editingController = new ImageEditingController(_cache);
            lstImages.ItemsSource = _cache.ImageList;

            _selectionController = new SelectionOverlayController(
                selectionCanvas, selectionRectangle, selectionEllipse, discordMaskOverlay,
                maskOverlayContainer, btnApplyMovableMask, btnCancelMovableMask,
                btnCropSelection, btnCancelSelection, scrollViewer);

            _panZoomController = new PanZoomController(scrollViewer, imgScale, imgTranslate)
            {
                IsEditingMaskPredicate = () => _selectionController != null && _selectionController.IsEditingMask
            };

            _sliderController = new SliderAdjustmentController(
                sliderBrightness, sliderContrast, sliderOpacity,
                txtBrightness, txtContrast, txtOpacity, imgPreview)
            {
                OnStateSaveRequested = SaveStateForUndo
            };
        }

        #region File I/O & Queue Management

        private void ButtonSelectImage_Click(object sender, RoutedEventArgs e)
        {
            string[]? files = _fileController.SelectImages();
            if (files != null)
            {
                _mainController.AddImages(files);
            }
        }

        private void ButtonSaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                try
                {
                    if (_fileController.SaveImage(bitmapSource))
                    {
                        MessageBox.Show("Ảnh đã được lưu thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi lưu ảnh: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Không có dữ liệu ảnh để lưu!");
            }
        }

        private async void ButtonExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (_cache.ImageList.Count == 0)
            {
                MessageBox.Show("Danh sách trống!");
                return;
            }

            var folderDialog = new OpenFolderDialog
            {
                Title = "Chọn thư mục lưu tất cả hình ảnh"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string outputFolder = folderDialog.FolderName;
                try
                {
                    await _fileController.ExportImagesAsync(_cache.ImageList, _cache.PendingImages, outputFolder);
                    MessageBox.Show($"Đã xuất {_cache.ImageList.Count} ảnh thành công tới {outputFolder}!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Có lỗi khi xuất: " + ex.Message);
                }
            }
        }

        private void ButtonRemoveImage_Click(object sender, RoutedEventArgs e)
        {
            _mainController.ClearImages();
            imgPreview.Source = null;
            txtFilePath.Text = "Chưa có ảnh nào được chọn";
        }

        private void lstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstImages?.SelectedItem is string selectedPath && !string.IsNullOrEmpty(selectedPath))
            {
                _panZoomController?.Reset();
                ShowImage(selectedPath);
            }
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string[] supported = { ".jpg", ".jpeg", ".png", ".bmp" };
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (supported.Contains(ext))
                    {
                        _cache.AddImage(file);
                    }
                }
            }
        }

        private void ShowImage(string path)
        {
            try
            {
                BitmapSource bitmap = _mainController.LoadImage(path);
                imgPreview.Source = bitmap;
                txtFilePath.Text = path;

                _cache.SetSliderBaseline(path, bitmap);
                _history.Clear();
                _sliderController.ResetSliders();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hiển thị ảnh: " + ex.Message);
            }
        }

        #endregion

        #region Undo & Redo Management

        private void SaveStateForUndo()
        {
            if (imgPreview.Source is BitmapSource source)
            {
                _mainController.SaveUndoState(source);
            }
        }

        private void Undo()
        {
            if (_mainController.CanUndo && imgPreview.Source is BitmapSource currentImage)
            {
                var undoneSource = _mainController.Undo(currentImage);
                if (undoneSource != null)
                {
                    imgPreview.Source = undoneSource;
                    string path = txtFilePath.Text;
                    if (!string.IsNullOrEmpty(path))
                    {
                        _cache.UpdateImage(path, undoneSource);
                        _cache.SetSliderBaseline(path, undoneSource);
                    }
                    _sliderController.ResetSliders();
                }
            }
        }

        private void Redo()
        {
            if (_mainController.CanRedo && imgPreview.Source is BitmapSource currentImage)
            {
                var redoneSource = _mainController.Redo(currentImage);
                if (redoneSource != null)
                {
                    imgPreview.Source = redoneSource;
                    string path = txtFilePath.Text;
                    if (!string.IsNullOrEmpty(path))
                    {
                        _cache.UpdateImage(path, redoneSource);
                        _cache.SetSliderBaseline(path, redoneSource);
                    }
                    _sliderController.ResetSliders();
                }
            }
        }

        private void ButtonUndo_Click(object sender, RoutedEventArgs e) => Undo();
        private void ButtonRedo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z) Undo();
                else if (e.Key == Key.Y) Redo();
            }
        }

        #endregion

        #region Core Filter Application Engine

        private async Task ApplyFilter(Func<Mat, Mat> filterLogic)
        {
            try
            {
                string file = txtFilePath.Text;
                if (imgPreview.Source is not BitmapSource || string.IsNullOrEmpty(file)) return;

                SaveStateForUndo();
                await _mainController.ApplyFilterAsync(
                    filterLogic,
                    cbApplyToAll?.IsChecked == true,
                    file,
                    (source, path, mat) =>
                    {
                        _cache.UpdateImage(path, source, mat);
                        if (path == file)
                        {
                            imgPreview.Source = source;
                            _cache.SetSliderBaseline(path, source);
                        }
                    },
                    _sliderController.ResetSliders);
            }
            catch (Exception ex)
            {
                _history.RemoveFirstUndo();
                MessageBox.Show("Lỗi xử lý ảnh: " + ex.Message);
            }
        }

        private async void ButtonGrayscale_Click(object sender, RoutedEventArgs e) => await ApplyFilter(ImageFilterService.ApplyGrayscale);
        private async void ButtonBlurImage_Click(object sender, RoutedEventArgs e) => await ApplyFilter(mat => ImageFilterService.ApplyGaussianBlur(mat, 15));
        private async void ButtonRotate_Click(object sender, RoutedEventArgs e) => await ApplyFilter(ImageFilterService.ApplyRotate90);
        private async void ButtonCanny_Click(object sender, RoutedEventArgs e) => await ApplyFilter(mat => ImageFilterService.ApplyCanny(mat, 100, 200));
        private async void ButtonSepia_Click(object sender, RoutedEventArgs e) => await ApplyFilter(ImageFilterService.ApplySepia);
        private void ButtonFilterAutumn_Click(object sender, RoutedEventArgs e) => ApplyColorMapFilter(ColorMapType.Autumn);
        private void ButtonFilterWinter_Click(object sender, RoutedEventArgs e) => ApplyColorMapFilter(ColorMapType.Winter);
        private void ButtonFilterRainbow_Click(object sender, RoutedEventArgs e) => ApplyColorMapFilter(ColorMapType.Rainbow);

        private async void ApplyColorMapFilter(ColorMapType mapType)
        {
            await ApplyFilter(mat => ImageFilterService.ApplyColorMap(mat, mapType));
        }

        private async void ButtonCurve_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                CurveWindow curveWin = new CurveWindow(bitmapSource)
                {
                    Owner = this
                };

                if (curveWin.ShowDialog() == true && curveWin.ResultLUTMat != null)
                {
                    using (Mat lutMat = curveWin.ResultLUTMat)
                    {
                        await ApplyFilter(mat => CurveLutService.ApplyLUT(mat, lutMat));
                    }
                }
            }
        }

        private async void MenuItemGHE_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFilter(mat => HistogramEnhancementService.ApplyGlobalHistogramEqualization(mat));
        }

        private async void MenuItemAHE_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFilter(mat => HistogramEnhancementService.ApplyAdaptiveHistogramEqualization(mat, 8, 8));
        }

        private async void MenuItemCLAHE_Click(object sender, RoutedEventArgs e)
        {
            await ApplyFilter(mat => HistogramEnhancementService.ApplyClahe(mat, 2.5, 8, 8));
        }

        private void MenuItemEnhancementDialog_Click(object sender, RoutedEventArgs e)
        {
            string file = txtFilePath?.Text ?? "";
            if (imgPreview.Source is BitmapSource && !string.IsNullOrEmpty(file) && file != "Chưa chọn ảnh")
            {
                using Mat currentMat = _cache.GetOrLoadMat(file);
                LowLightEnhanceWindow enhanceWin = new LowLightEnhanceWindow(currentMat)
                {
                    Owner = this
                };

                enhanceWin.ShowDialog();

                if (enhanceWin.IsApplied && enhanceWin.ResultMat != null)
                {
                    using Mat resultMat = enhanceWin.ResultMat;
                    SaveStateForUndo();
                    var newSource = ImageConversionHelper.ToBitmapSource(resultMat);
                    newSource?.Freeze();

                    if (newSource != null)
                    {
                        imgPreview.Source = newSource;
                        _cache.UpdateImage(file, newSource, resultMat);
                        _cache.SetSliderBaseline(file, newSource);
                        _sliderController?.ResetSliders();
                    }
                }
            }
            else
            {
                MessageBox.Show("Vui lòng chọn hoặc mở một ảnh trước khi mở cửa sổ tăng cường sáng!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Sliders Event Wrappers

        private void Slider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _sliderController?.HandleDragStarted(txtFilePath?.Text ?? "");
        private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _sliderController?.HandleDragCompleted(txtFilePath?.Text ?? "");
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _sliderController?.HandleValueChanged(txtFilePath?.Text ?? "");

        private async void TxtValues_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _sliderController != null) await _sliderController.HandleTextSubmitAsync(txtFilePath?.Text ?? "");
        }

        private async void TxtValues_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_sliderController != null) await _sliderController.HandleTextSubmitAsync(txtFilePath?.Text ?? "");
        }

        #endregion

        #region Masking & Crop Operations

        private void maskOverlayContainer_SizeChanged(object sender, SizeChangedEventArgs e) => _selectionController?.UpdateDiscordMaskOverlay();
        private void ButtonMaskCircle_Click(object sender, RoutedEventArgs e) { if (imgPreview.Source != null) _selectionController.StartMovableMask(true); }
        private void ButtonMaskRectangle_Click(object sender, RoutedEventArgs e) { if (imgPreview.Source != null) _selectionController.StartMovableMask(false); }
        private void ButtonCancelMovableMask_Click(object? sender, RoutedEventArgs? e) => _selectionController.CancelMovableMask(imgTranslate);

        private async void ButtonApplyMovableMask_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                try
                {
                    SaveStateForUndo();

                    WPFPoint centerOnOverlay = new WPFPoint(maskOverlayContainer.ActualWidth / 2, maskOverlayContainer.ActualHeight / 2);
                    WPFPoint centerOnImage = maskOverlayContainer.TranslatePoint(centerOnOverlay, imgPreview);
                    WPFPoint edgeOnOverlay = new WPFPoint(centerOnOverlay.X + 150, centerOnOverlay.Y);
                    WPFPoint edgeOnImage = maskOverlayContainer.TranslatePoint(edgeOnOverlay, imgPreview);

                    double radiusInImageUnits = Math.Abs(edgeOnImage.X - centerOnImage.X);
                    double scaleX = bitmapSource.PixelWidth / imgPreview.ActualWidth;
                    double scaleY = bitmapSource.PixelHeight / imgPreview.ActualHeight;

                    int realCenterX = (int)(centerOnImage.X * scaleX);
                    int realCenterY = (int)(centerOnImage.Y * scaleY);
                    int realRadius = (int)(radiusInImageUnits * scaleX);
                    string file = txtFilePath.Text;

                    var result = await _editingController.ApplyMovableMaskAsync(
                        file, realCenterX, realCenterY, realRadius, _selectionController.CurrentMaskIsCircle);

                    if (result.Source != null)
                    {
                        imgPreview.Source = result.Source;
                        if (!string.IsNullOrEmpty(file))
                        {
                            _cache.UpdateImage(file, result.Source, result.Mat);
                            _cache.SetSliderBaseline(file, result.Source);
                            _sliderController.ResetSliders();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _history.RemoveFirstUndo();
                    MessageBox.Show("Lỗi CROP mask: " + ex.Message);
                }
                finally
                {
                    ButtonCancelMovableMask_Click(null, null);
                }
            }
        }

        private void ButtonSelectRect_Click(object sender, RoutedEventArgs e) { if (imgPreview.Source != null) _selectionController.StartSelection(false, false); }
        private void ButtonSelectEllipse_Click(object sender, RoutedEventArgs e) { if (imgPreview.Source != null) _selectionController.StartSelection(true, false); }
        private void ButtonSelectObject_Click(object sender, RoutedEventArgs e) { if (imgPreview.Source != null) _selectionController.StartSelection(false, true); }
        private void ButtonCancelSelection_Click(object? sender, RoutedEventArgs? e) => _selectionController.CancelSelection();

        private async void ButtonCropSelection_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                var safeCropRect = _selectionController.GetSafeCropRect(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                if (!safeCropRect.HasValue) return;

                try
                {
                    SaveStateForUndo();
                    string file = txtFilePath.Text;
                    var result = await _editingController.ApplySelectionAsync(
                        file, safeCropRect.Value, _selectionController.IsEllipseSelection, _selectionController.IsObjectSelection);

                    if (result.Source != null)
                    {
                        imgPreview.Source = result.Source;
                        if (!string.IsNullOrEmpty(file))
                        {
                            _cache.UpdateImage(file, result.Source, result.Mat);
                            _cache.SetSliderBaseline(file, result.Source);
                            _sliderController.ResetSliders();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _history.RemoveFirstUndo();
                    MessageBox.Show("Lỗi CROP ảnh theo vùng chọn: " + ex.Message);
                }
                finally
                {
                    ButtonCancelSelection_Click(null, null);
                }
            }
        }

        private async void ButtonMaskImage_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                    Title = "Chọn ảnh dùng làm mặt nạ (Ảnh trắng đen / Gray)"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        SaveStateForUndo();
                        string file = txtFilePath.Text;
                        string maskPath = openFileDialog.FileName;

                        var result = await _editingController.ApplyImageMaskAsync(file, maskPath);

                        if (result.Source != null)
                        {
                            imgPreview.Source = result.Source;
                            if (!string.IsNullOrEmpty(file))
                            {
                                _cache.UpdateImage(file, result.Source, result.Mat);
                                _cache.SetSliderBaseline(file, result.Source);
                                _sliderController.ResetSliders();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _history.RemoveFirstUndo();
                        MessageBox.Show("Lỗi tạo mặt nạ từ ảnh: " + ex.Message);
                    }
                }
            }
        }

        #endregion

        #region Augmentation & Preview Windows

        private void ButtonAugmentation_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                AugmentWindow augWin = new AugmentWindow(bitmapSource)
                {
                    Owner = this
                };

                if (augWin.ShowDialog() == true && augWin.ResultSource != null)
                {
                    SaveStateForUndo();
                    string originalPath = txtFilePath.Text;

                    imgPreview.Source = augWin.ResultSource;
                    if (!string.IsNullOrEmpty(originalPath))
                    {
                        Mat? firstMat = augWin.ResultMats.Count > 0 ? augWin.ResultMats[0] : null;
                        _cache.UpdateImage(originalPath, augWin.ResultSource, firstMat);
                        _cache.SetSliderBaseline(originalPath, augWin.ResultSource);
                    }

                    if (augWin.ResultSources.Count > 1)
                    {
                        string directory = Path.GetDirectoryName(originalPath) ?? "";
                        string fileName = Path.GetFileNameWithoutExtension(originalPath);
                        string extension = Path.GetExtension(originalPath);

                        for (int i = 1; i < augWin.ResultSources.Count; i++)
                        {
                            int suffix = i;
                            string newPath = Path.Combine(directory, $"{fileName}_aug_{suffix}{extension}");
                            while (_cache.ImageList.Contains(newPath) || File.Exists(newPath))
                            {
                                suffix++;
                                newPath = Path.Combine(directory, $"{fileName}_aug_{suffix}{extension}");
                            }

                            Mat? currentMat = augWin.ResultMats.Count > i ? augWin.ResultMats[i] : null;
                            _cache.UpdateImage(newPath, augWin.ResultSources[i], currentMat);
                            _cache.AddImage(newPath);
                        }
                    }

                    _sliderController.ResetSliders();
                }
            }
        }

        private void ButtonPreview_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source != null)
            {
                PreviewWindow previewWin = _previewController.CreatePreviewWindow(imgPreview.Source);
                previewWin.Owner = this;
                previewWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                previewWin.Show();
            }
            else
            {
                MessageBox.Show("Vui lòng chọn ảnh trước khi xem trước!");
            }
        }

        private void ButtonPreviewAll_Click(object sender, RoutedEventArgs e)
        {
            if (_cache.ImageList.Count == 0)
            {
                MessageBox.Show("Danh sách trống!");
                return;
            }

            List<BitmapSource> previews = new List<BitmapSource>();
            foreach (var file in _cache.ImageList)
            {
                previews.Add(_cache.GetOrLoadBitmap(file));
            }

            PreviewAllWindow win = _previewController.CreatePreviewAllWindow(previews);
            win.Owner = this;

            if (win.ShowDialog() == true && win.SelectedImage != null)
            {
                string? foundFile = null;
                foreach (var file in _cache.ImageList)
                {
                    if (_cache.PendingImages.TryGetValue(file, out var source) && source == win.SelectedImage)
                    {
                        foundFile = file;
                        break;
                    }
                }

                if (foundFile != null)
                {
                    ShowImage(foundFile);
                }
                else
                {
                    imgPreview.Source = win.SelectedImage;
                    _cache.SetSliderBaseline(txtFilePath.Text, win.SelectedImage);
                }
            }
        }

        #endregion

        #region Canvas & Mouse Navigation Event Handlers

        private void Image_MouseWheel(object sender, MouseWheelEventArgs e) => _panZoomController?.HandleMouseWheel(e);

        private void scrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectionController?.HandleMouseDown(e) == true) return;
            _panZoomController?.HandleMouseDown(e);
        }

        private void scrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_selectionController?.HandleMouseMove(e) == true) return;
            _panZoomController?.HandleMouseMove(e);
        }

        private void scrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_selectionController?.HandleMouseUp() == true) return;
            _panZoomController?.HandleMouseUp();
        }

        private void MenuItemNew_Click(object sender, RoutedEventArgs e)
        {
            MainWindow newWindow = new MainWindow();
            newWindow.Show();
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion
    }
}