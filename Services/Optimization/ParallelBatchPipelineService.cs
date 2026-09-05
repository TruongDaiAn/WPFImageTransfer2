using Emgu.CV;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;

namespace WPFImageTransfer.Services.Optimization
{
    public class ProcessedImageResult
    {
        public string FilePath { get; set; } = string.Empty;
        public BitmapSource? Source { get; set; }
        public Mat? Mat { get; set; }
    }

    /// <summary>
    /// Module tối ưu hóa: Đường ống xử lý song song phân tán (Parallel Batch Pipeline)
    /// Xử lý hàng loạt ảnh (Apply to All) và xuất tệp hàng loạt với tối đa hiệu năng CPU.
    /// </summary>
    public static class ParallelBatchPipelineService
    {
        /// <summary>
        /// Xử lý bộ lọc cho danh sách hàng trăm ảnh song song đa nhân.
        /// </summary>
        public static async Task<List<ProcessedImageResult>> ProcessImagesParallelAsync(
            IEnumerable<string> filePaths,
            Func<Mat, Mat> filterLogic,
            ImageCacheManager cache,
            IProgress<int>? progress = null)
        {
            var list = new List<string>(filePaths);
            if (list.Count == 0) return new List<ProcessedImageResult>();

            var results = new ProcessedImageResult[list.Count];
            int completedCount = 0;
            int total = list.Count;

            await Parallel.ForEachAsync(
                list.Select((path, index) => (Path: path, Index: index)),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (item, token) =>
                {
                    await Task.Run(() =>
                    {
                        using (Mat mat = cache.GetOrLoadMat(item.Path))
                        {
                            Mat processed = filterLogic(mat);
                            var newSource = ImageConversionHelper.ToBitmapSource(processed);
                            newSource?.Freeze();

                            results[item.Index] = new ProcessedImageResult
                            {
                                FilePath = item.Path,
                                Source = newSource,
                                Mat = processed
                            };
                        }

                        int done = Interlocked.Increment(ref completedCount);
                        progress?.Report((int)((done / (double)total) * 100));
                    }, token);
                });

            return new List<ProcessedImageResult>(results);
        }

        /// <summary>
        /// Xuất danh sách ảnh ra ổ cứng song song đa luồng cực nhanh.
        /// </summary>
        public static async Task ExportImagesParallelAsync(
            IEnumerable<string> imageList,
            Dictionary<string, BitmapSource> pendingImages,
            string outputFolder,
            IProgress<int>? progress = null)
        {
            var list = new List<string>(imageList);
            if (list.Count == 0) return;

            int completedCount = 0;
            int total = list.Count;

            await Parallel.ForEachAsync(
                list,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
                async (file, token) =>
                {
                    await Task.Run(() =>
                    {
                        string fileName = Path.GetFileName(file);
                        string outPath = Path.Combine(outputFolder, "Exported_" + fileName);

                        if (pendingImages.TryGetValue(file, out var source) && source != null)
                        {
                            BitmapEncoder encoder = GetEncoderByExtension(file);
                            encoder.Frames.Add(BitmapFrame.Create(source));
                            using (var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                            {
                                encoder.Save(fileStream);
                            }
                        }
                        else
                        {
                            File.Copy(file, outPath, true);
                        }

                        int done = Interlocked.Increment(ref completedCount);
                        progress?.Report((int)((done / (double)total) * 100));
                    }, token);
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
