using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Services;

namespace WPFImageTransfer.Converters
{
    /// <summary>
    /// Chuyển đổi đường dẫn ảnh thành Thumbnail để hiển thị mượt mà trên danh sách ListBox.
    /// Ưu tiên lấy ảnh trong bộ nhớ cache nếu ảnh đã qua chỉnh sửa.
    /// </summary>
    public class ThumbnailConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
                return null;

            // Kiểm tra trong bộ nhớ cache ảnh đang chỉnh sửa
            if (ImageCacheManager.Instance.PendingImages.TryGetValue(path, out var cachedSource) && cachedSource != null)
            {
                return cachedSource;
            }

            // Kiểm tra trong hàng đợi thumbnail nạp ngầm
            if (WPFImageTransfer.Services.Optimization.AsyncThumbnailQueue.Instance.TryGet(path, out var queuedThumb) && queuedThumb != null)
            {
                return queuedThumb;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 100; // Giảm kích thước để tiết kiệm RAM tối đa
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
