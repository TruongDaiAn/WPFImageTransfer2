using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Services.Optimization;

namespace WPFImageTransfer.Services
{
    /// <summary>
    /// Quản lý danh sách ảnh, cache BitmapSource và quản lý vòng đời bộ nhớ OpenCV Mat
    /// kết hợp thuật toán tối ưu hóa bộ nhớ LRU (Least Recently Used) và nạp trước Thumbnail ngầm.
    /// </summary>
    public class ImageCacheManager
    {
        private static ImageCacheManager? _instance;
        public static ImageCacheManager Instance => _instance ??= new ImageCacheManager();

        public ObservableCollection<string> ImageList { get; } = new ObservableCollection<string>();
        public Dictionary<string, BitmapSource> PendingImages { get; } = new Dictionary<string, BitmapSource>();
        public Dictionary<string, Mat> CacheMats { get; } = new Dictionary<string, Mat>();

        private readonly LruMemoryCacheManager _lruManager = new LruMemoryCacheManager(600); // Ngưỡng trần 600MB

        public Mat? LatestAdjustedMat { get; set; }
        public Mat? OriginalMatForSliders { get; set; }
        public BitmapSource? OriginalImageForSliders { get; set; }

        public void AddImage(string path)
        {
            if (!ImageList.Contains(path))
            {
                ImageList.Add(path);
                // Ảnh kết quả augmentation có thể chỉ tồn tại trong cache, không phải file trên đĩa.
                if (!PendingImages.ContainsKey(path))
                {
                    AsyncThumbnailQueue.Instance.Enqueue(path);
                }
            }
        }

        public BitmapSource GetOrLoadBitmap(string path)
        {
            if (PendingImages.TryGetValue(path, out var cachedSource) && cachedSource != null)
            {
                return cachedSource;
            }

            if (CacheMats.TryGetValue(path, out var cachedMat) && cachedMat != null && !cachedMat.IsEmpty)
            {
                var converted = ImageConversionHelper.ToBitmapSource(cachedMat);
                if (converted != null)
                {
                    PendingImages[path] = converted;
                    return converted;
                }
            }

            var loaded = ImageConversionHelper.ToBitmapSourceFromFile(path);
            PendingImages[path] = loaded;
            return loaded;
        }

        public Mat GetOrLoadMat(string path)
        {
            if (_lruManager.TryGet(path, out var lruMat) && lruMat != null)
            {
                return lruMat;
            }

            if (CacheMats.TryGetValue(path, out var cachedMat) && cachedMat != null && !cachedMat.IsEmpty)
            {
                return cachedMat.Clone();
            }

            BitmapSource source = GetOrLoadBitmap(path);
            Mat mat = ImageConversionHelper.BitmapSourceToMat(source);
            _lruManager.Put(path, mat);
            CacheMats[path] = mat.Clone();
            return mat;
        }

        public void UpdateImage(string path, BitmapSource source, Mat? mat = null)
        {
            PendingImages[path] = source;

            if (CacheMats.TryGetValue(path, out var oldMat) && oldMat != null)
            {
                oldMat.Dispose();
            }

            if (mat != null)
            {
                CacheMats[path] = mat.Clone();
                _lruManager.Put(path, mat);
            }
            else
            {
                var createdMat = ImageConversionHelper.BitmapSourceToMat(source);
                CacheMats[path] = createdMat;
                _lruManager.Put(path, createdMat);
            }
        }

        public void ClearSliderBaseline()
        {
            OriginalImageForSliders = null;
            if (OriginalMatForSliders != null)
            {
                OriginalMatForSliders.Dispose();
                OriginalMatForSliders = null;
            }
        }

        public void SetSliderBaseline(string path, BitmapSource currentSource)
        {
            OriginalImageForSliders = currentSource;
            if (OriginalMatForSliders != null)
            {
                OriginalMatForSliders.Dispose();
                OriginalMatForSliders = null;
            }

            if (!string.IsNullOrEmpty(path) && CacheMats.TryGetValue(path, out var cachedMat) && cachedMat != null)
            {
                OriginalMatForSliders = cachedMat.Clone();
            }
            else
            {
                OriginalMatForSliders = ImageConversionHelper.BitmapSourceToMat(currentSource);
            }
        }

        public void Clear()
        {
            ImageList.Clear();
            PendingImages.Clear();
            _lruManager.Clear();
            AsyncThumbnailQueue.Instance.Clear();

            foreach (var mat in CacheMats.Values)
            {
                mat?.Dispose();
            }
            CacheMats.Clear();

            if (LatestAdjustedMat != null)
            {
                LatestAdjustedMat.Dispose();
                LatestAdjustedMat = null;
            }

            ClearSliderBaseline();
        }
    }
}
