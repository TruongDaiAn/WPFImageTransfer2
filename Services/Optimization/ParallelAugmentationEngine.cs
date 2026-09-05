using Emgu.CV;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Services.Optimization
{
    /// <summary>
    /// Module tối ưu hóa: Sinh hàng loạt ảnh Augmentation (lên tới 2.000 ảnh) siêu tốc
    /// bằng cách phân phối song song đa nhân CPU (Multi-core Parallel Processing).
    /// </summary>
    public static class ParallelAugmentationEngine
    {
        public class BatchAugmentResult
        {
            public List<BitmapSource> Sources { get; set; } = new List<BitmapSource>();
            public List<Mat> Mats { get; set; } = new List<Mat>();
        }

        /// <summary>
        /// Sinh bộ ảnh ngẫu nhiên đa luồng tận dụng 100% nhân CPU.
        /// </summary>
        public static async Task<BatchAugmentResult> GenerateParallelBatchAsync(
            Mat originalMat, 
            int count, 
            AugmentationParameters param, 
            IProgress<int>? progress = null)
        {
            return await Task.Run(() =>
            {
                // Mảng cố định theo kích thước để ghi dữ liệu song song không cần lock
                Mat[] resultMats = new Mat[count];
                BitmapSource[] resultSources = new BitmapSource[count];

                int completedCount = 0;

                // Sử dụng Parallel.For chia việc cho các luồng phần cứng
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                {
                    // Mỗi luồng có một Random seed độc lập theo thread id và index để tránh trùng lặp
                    Random rand = new Random(Guid.NewGuid().GetHashCode() + i);

                    bool currentFlipH = param.FlipH && (rand.Next(2) == 0);
                    bool currentFlipV = param.FlipV && (rand.Next(2) == 0);

                    double currentRotateAngle = 0;
                    if (param.RotateAngle != 0)
                    {
                        double minA = Math.Min(-param.RotateAngle, param.RotateAngle);
                        double maxA = Math.Max(-param.RotateAngle, param.RotateAngle);
                        currentRotateAngle = minA + rand.NextDouble() * (maxA - minA);
                    }

                    double currentShearAngle = 0;
                    if (param.ShearAngle != 0)
                    {
                        double minS = Math.Min(-param.ShearAngle, param.ShearAngle);
                        double maxS = Math.Max(-param.ShearAngle, param.ShearAngle);
                        currentShearAngle = minS + rand.NextDouble() * (maxS - minS);
                    }

                    double currentShiftX = 0;
                    if (param.ShiftXPercent != 0)
                    {
                        double minX = Math.Min(-param.ShiftXPercent, param.ShiftXPercent);
                        double maxX = Math.Max(-param.ShiftXPercent, param.ShiftXPercent);
                        currentShiftX = minX + rand.NextDouble() * (maxX - minX);
                    }

                    double currentShiftY = 0;
                    if (param.ShiftYPercent != 0)
                    {
                        double minY = Math.Min(-param.ShiftYPercent, param.ShiftYPercent);
                        double maxY = Math.Max(-param.ShiftYPercent, param.ShiftYPercent);
                        currentShiftY = minY + rand.NextDouble() * (maxY - minY);
                    }

                    // Biến đổi Affine
                    Mat transformedMat = ImageAugmentationService.ApplyTransform(
                        originalMat, currentFlipH, currentFlipV, currentRotateAngle, currentShearAngle, currentShiftX, currentShiftY);

                    // Chuyển sang BitmapSource và Freeze ngay trong luồng nền
                    var source = ImageConversionHelper.ToBitmapSource(transformedMat);
                    source?.Freeze();

                    resultMats[i] = transformedMat;
                    if (source != null) resultSources[i] = source;

                    int current = System.Threading.Interlocked.Increment(ref completedCount);
                    if (current % 20 == 0 || current == count)
                    {
                        progress?.Report((int)((current / (double)count) * 100));
                    }
                });

                return new BatchAugmentResult
                {
                    Mats = new List<Mat>(resultMats),
                    Sources = new List<BitmapSource>(resultSources)
                };
            });
        }
    }
}
