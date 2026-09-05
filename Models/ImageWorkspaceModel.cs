using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Services;

namespace WPFImageTransfer.Models
{
    public sealed class ImageWorkspaceModel
    {
        public ImageCacheManager Cache { get; } = ImageCacheManager.Instance;
        public ImageHistoryManager History { get; } = new ImageHistoryManager();
        public ObservableCollection<string> ImageList => Cache.ImageList;
        public Dictionary<string, BitmapSource> PendingImages => Cache.PendingImages;
    }
}