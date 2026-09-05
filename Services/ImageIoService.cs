using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer.Services
{
    /// <summary>
    /// Chuyên trách xử lý I/O: Mở ảnh, lưu ảnh đơn lẻ và xuất hàng loạt danh sách ảnh.
    /// </summary>
    public static class ImageIoService
    {
        public static string[]? OpenImagesDialog()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                return openFileDialog.FileNames;
            }
            return null;
        }

        public static bool SaveSingleImage(BitmapSource bitmapSource)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                Title = "Lưu ảnh sau khi chỉnh sửa",
                FileName = "EditedImage"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                using (var fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    BitmapEncoder encoder = GetEncoderByExtension(saveFileDialog.FileName);
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }
                return true;
            }
            return false;
        }

        public static async Task ExportAllImagesAsync(IEnumerable<string> imageList, Dictionary<string, BitmapSource> pendingImages, string outputFolder)
        {
            await Task.Run(() =>
            {
                foreach (string file in imageList)
                {
                    string fileName = Path.GetFileName(file);
                    string outPath = Path.Combine(outputFolder, "Exported_" + fileName);

                    if (pendingImages.TryGetValue(file, out var source) && source != null)
                    {
                        BitmapEncoder encoder = GetEncoderByExtension(file);
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        using (var fileStream = new FileStream(outPath, FileMode.Create))
                        {
                            encoder.Save(fileStream);
                        }
                    }
                    else
                    {
                        File.Copy(file, outPath, true);
                    }
                }
            });
        }

        private static BitmapEncoder GetEncoderByExtension(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
        }
    }
}
