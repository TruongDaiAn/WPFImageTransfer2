using Microsoft.Win32;
using System;
using System.Collections.ObjectModel; 
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<string> ImageList { get; set; } = new ObservableCollection<string>();

        private Point _lastMousePosition;
        private bool _isDragging = false;

        public MainWindow()
        {
            InitializeComponent();

            lstImages.ItemsSource = ImageList;
        }

        private void ButtonSelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true; 
            openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    AddImageToList(filePath);
                }
            }
        }

        private void ButtonSaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source is BitmapSource bitmapSource)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp";
                saveFileDialog.Title = "Lưu ảnh sau khi chỉnh sửa";
                saveFileDialog.FileName = "EditedImage";

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        using (var fileStream = new System.IO.FileStream(saveFileDialog.FileName, System.IO.FileMode.Create))
                        {
                            BitmapEncoder encoder = null;

                            string extension = System.IO.Path.GetExtension(saveFileDialog.FileName).ToLower();
                            switch (extension)
                            {
                                case ".png": encoder = new PngBitmapEncoder(); break;
                                case ".jpg":
                                case ".jpeg": encoder = new JpegBitmapEncoder(); break;
                                case ".bmp": encoder = new BmpBitmapEncoder(); break;
                                default: encoder = new PngBitmapEncoder(); break;
                            }

                            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                            encoder.Save(fileStream);
                        }
                        MessageBox.Show("Ảnh đã được lưu thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi khi lưu ảnh: " + ex.Message);
                    }
                }
            }
            else
            {
                MessageBox.Show("Không có dữ liệu ảnh để lưu!");
            }
        }
        private void Image_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double zoomStep = 1.1;

            if (e.Delta > 0)
            {
                imgScale.ScaleX *= zoomStep;
                imgScale.ScaleY *= zoomStep;
            }
            else
            {
                if (imgScale.ScaleX > 0.1)
                {
                    imgScale.ScaleX /= zoomStep;
                    imgScale.ScaleY /= zoomStep;
                }
            }
            e.Handled = true;
        }

        private void scrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (imgScale.ScaleX > 1)
            {
                _lastMousePosition = e.GetPosition(scrollViewer);
                _isDragging = true;
                scrollViewer.Cursor = Cursors.Hand; 
                scrollViewer.CaptureMouse();
            }
        }

        private void scrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(scrollViewer);

                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - deltaX);
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - deltaY);

                _lastMousePosition = currentPosition;
            }
        }

        private void scrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            scrollViewer.Cursor = Cursors.Arrow;
            scrollViewer.ReleaseMouseCapture();
        }
        private void lstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstImages.SelectedItem != null)
            {
                string selectedPath = lstImages.SelectedItem.ToString();

                if (imgScale != null)
                {
                    imgScale.ScaleX = 1;
                    imgScale.ScaleY = 1;
                }

                ShowImage(selectedPath);
            }
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void DropArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file).ToLower();
                    string[] supported = { ".jpg", ".jpeg", ".png", ".bmp" };

                    if (supported.Contains(extension))
                    {
                        AddImageToList(file);
                    }
                }
            }
        }

        private void ButtonRemoveImage_Click(object sender, RoutedEventArgs e)
        {
            ImageList.Clear();
            imgPreview.Source = null;
            txtFilePath.Text = "Chưa có ảnh nào được chọn";
        }

        private void ButtonPreview_Click(object sender, RoutedEventArgs e)
        {
            if (imgPreview.Source != null)
            {
                PreviewWindow previewWin = new PreviewWindow(imgPreview.Source);
                previewWin.Owner = this;
                previewWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                previewWin.Show();
            }
            else
            {
                MessageBox.Show("Vui lòng chọn ảnh trước khi xem trước!");
            }
        }

        private void AddImageToList(string path)
        {
            if (!ImageList.Contains(path))
            {
                ImageList.Add(path);
            }
        }

        private void ShowImage(string path)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();

                imgPreview.Source = bitmap;
                txtFilePath.Text = path;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hiển thị ảnh: " + ex.Message);
            }
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
    }
}