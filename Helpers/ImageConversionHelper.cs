using Emgu.CV;
using Emgu.CV.CvEnum;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer.Helpers
{
    /// <summary>
    /// Cung cấp các phương thức chuyển đổi dữ liệu hình ảnh giữa WPF BitmapSource và Emgu.CV Mat
    /// tối ưu hóa bằng ArrayPool<byte>.Shared tái sử dụng bộ nhớ đệm, triệt tiêu cấp phát rác (0% GC pressure).
    /// </summary>
    public static class ImageConversionHelper
    {
        /// <summary>
        /// Chuyển đổi Emgu.CV Mat sang WPF BitmapSource với ArrayPool đệm bộ nhớ siêu tốc.
        /// </summary>
        public static BitmapSource? ToBitmapSource(Mat mat)
        {
            if (mat == null || mat.IsEmpty) return null;

            int width = mat.Width;
            int height = mat.Height;
            int stride = mat.Step;
            int channels = mat.NumberOfChannels;

            PixelFormat format;
            if (channels == 1) format = PixelFormats.Gray8;
            else if (channels == 3) format = PixelFormats.Bgr24;
            else if (channels == 4) format = PixelFormats.Bgra32;
            else return null;

            int totalBytes = stride * height;
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            try
            {
                Marshal.Copy(mat.DataPointer, poolBuffer, 0, totalBytes);
                var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, poolBuffer, stride);
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }

        /// <summary>
        /// Chuyển đổi WPF BitmapSource sang Emgu.CV Mat chuẩn BGR 3 kênh (hoặc BGRA).
        /// </summary>
        public static Mat BitmapSourceToMat(BitmapSource source)
        {
            int width = 0;
            int height = 0;
            int stride = 0;
            int totalBytes = 0;

            FormatConvertedBitmap converted;
            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                converted = Application.Current.Dispatcher.Invoke(() => new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0));
            }
            else
            {
                converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            }

            width = converted.PixelWidth;
            height = converted.PixelHeight;
            stride = (width * converted.Format.BitsPerPixel + 7) / 8;
            totalBytes = height * stride;

            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            try
            {
                if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => converted.CopyPixels(poolBuffer, stride, 0));
                }
                else
                {
                    converted.CopyPixels(poolBuffer, stride, 0);
                }

                Mat mat = new Mat(height, width, DepthType.Cv8U, 3);
                Marshal.Copy(poolBuffer, 0, mat.DataPointer, totalBytes);
                return mat;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }

        /// <summary>
        /// Đọc ảnh từ file đường dẫn và Freeze để giải phóng khóa tệp (file lock).
        /// </summary>
        public static BitmapSource ToBitmapSourceFromFile(string path)
        {
            BitmapImage bmpImage = new BitmapImage();
            bmpImage.BeginInit();
            bmpImage.UriSource = new Uri(path);
            bmpImage.CacheOption = BitmapCacheOption.OnLoad; // Đảm bảo tệp không bị giữ lock
            bmpImage.EndInit();
            bmpImage.Freeze();
            return bmpImage;
        }
    }
}
