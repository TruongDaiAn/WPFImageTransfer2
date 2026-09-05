using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;

namespace WPFImageTransfer.Services.Optimization
{
    /// <summary>
    /// Module tối ưu hóa: Hàng đợi nạp Thumbnail ngầm (Async Thumbnail Queue)
    /// Sử dụng C# Channel để nạp trước thumbnail nền mượt mà, phản hồi ngay lập tức cho ListBox.
    /// </summary>
    public class AsyncThumbnailQueue
    {
        private static AsyncThumbnailQueue? _instance;
        public static AsyncThumbnailQueue Instance => _instance ??= new AsyncThumbnailQueue();

        private readonly Channel<string> _channel;
        private readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new ConcurrentDictionary<string, BitmapSource>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action<string, BitmapSource>? OnThumbnailLoaded;

        public AsyncThumbnailQueue()
        {
            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true
            });

            _ = StartBackgroundWorkerAsync(_cts.Token);
        }

        public void Enqueue(string imagePath)
        {
            if (!File.Exists(imagePath)) return;
            if (_thumbnailCache.ContainsKey(imagePath)) return;
            _channel.Writer.TryWrite(imagePath);
        }

        public bool TryGet(string imagePath, out BitmapSource? source)
        {
            return _thumbnailCache.TryGetValue(imagePath, out source);
        }

        private async Task StartBackgroundWorkerAsync(CancellationToken token)
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var path))
                {
                    if (token.IsCancellationRequested) break;

                    if (!_thumbnailCache.ContainsKey(path))
                    {
                        try
                        {
                            var thumbnail = await Task.Run(() =>
                            {
                                BitmapImage img = new BitmapImage();
                                img.BeginInit();
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.UriSource = new Uri(path);
                                img.DecodePixelWidth = 100;
                                img.EndInit();
                                img.Freeze();
                                return img;
                            }, token);

                            _thumbnailCache[path] = thumbnail;
                            OnThumbnailLoaded?.Invoke(path, thumbnail);
                        }
                        catch
                        {
                            // Bỏ qua các file ảnh hỏng
                        }
                    }
                }
            }
        }

        public void Clear()
        {
            _thumbnailCache.Clear();
        }
    }
}
