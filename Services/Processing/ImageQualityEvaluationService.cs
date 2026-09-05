using Emgu.CV;
using Emgu.CV.CvEnum;
using System;

namespace WPFImageTransfer.Services.Processing
{
    public sealed record ImageQualityMetric(string Name, string Value, string Direction, string Note);

    public sealed record ImageQualityEvaluation(
        ImageQualityMetric Entropy,
        ImageQualityMetric Loe,
        ImageQualityMetric Psnr,
        ImageQualityMetric Ssim,
        ImageQualityMetric Niqe,
        ImageQualityMetric Brisque);

    public static class ImageQualityEvaluationService
    {
        public static ImageQualityEvaluation Evaluate(Mat original, Mat enhanced, Mat? reference = null)
        {
            double originalEntropy = CalculateEntropy(original);
            double enhancedEntropy = CalculateEntropy(enhanced);
            double loe = CalculateLoe(original, enhanced);
            ImageQualityMetric psnr = new("PSNR", "Chưa có ảnh chuẩn", "↑", "Cần ảnh tham chiếu chất lượng cao");
            ImageQualityMetric ssim = new("SSIM", "Chưa có ảnh chuẩn", "↑", "Cần ảnh tham chiếu chất lượng cao");
            if (reference != null && !reference.IsEmpty)
            {
                psnr = new("PSNR", $"{CalculatePsnr(reference, enhanced):0.000} dB", "↑", "So với ảnh tham chiếu");
                ssim = new("SSIM", $"{CalculateSsim(reference, enhanced):0.0000}", "↑", "So với ảnh tham chiếu");
            }

            return new ImageQualityEvaluation(
                new("Entropy", $"{enhancedEntropy:0.0000} bit", "↑*", $"Ảnh gốc: {originalEntropy:0.0000} bit"),
                new("LOE", $"{loe:0.0000}", "↓", "Bảo toàn thứ tự độ sáng"), psnr, ssim,
                new("NIQE", "Chưa tích hợp", "↓", "Cần mô hình thống kê NIQE"),
                new("BRISQUE", "Chưa tích hợp", "↓", "Cần mô hình thống kê BRISQUE"));
        }

        private static Mat ToGray(Mat source)
        {
            Mat gray = new();
            if (source.NumberOfChannels == 1) source.CopyTo(gray);
            else CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
            return gray;
        }

        public static double CalculateEntropy(Mat source)
        {
            using Mat gray = ToGray(source);
            int[] histogram = HistogramEnhancementService.CalculateLuminanceHistogram(gray);
            double total = gray.Width * (double)gray.Height;
            double entropy = 0;
            foreach (int count in histogram)
            {
                if (count == 0) continue;
                double probability = count / total;
                entropy -= probability * Math.Log2(probability);
            }
            return entropy;
        }

        public static double CalculatePsnr(Mat reference, Mat enhanced)
        {
            using Mat referenceGray = ToGray(reference);
            using Mat enhancedGray = ToGray(enhanced);
            EnsureSameSize(referenceGray, enhancedGray);
            double squaredError = 0;
            unsafe
            {
                byte* referencePtr = (byte*)referenceGray.DataPointer;
                byte* enhancedPtr = (byte*)enhancedGray.DataPointer;
                for (int y = 0; y < referenceGray.Height; y++)
                {
                    byte* referenceRow = referencePtr + y * referenceGray.Step;
                    byte* enhancedRow = enhancedPtr + y * enhancedGray.Step;
                    for (int x = 0; x < referenceGray.Width; x++)
                    {
                        double difference = referenceRow[x] - enhancedRow[x];
                        squaredError += difference * difference;
                    }
                }
            }
            double mse = squaredError / (referenceGray.Width * (double)referenceGray.Height);
            return mse <= double.Epsilon ? double.PositiveInfinity : 10 * Math.Log10(255 * 255 / mse);
        }

        public static double CalculateSsim(Mat reference, Mat enhanced)
        {
            using Mat referenceGray = ToGray(reference);
            using Mat enhancedGray = ToGray(enhanced);
            EnsureSameSize(referenceGray, enhancedGray);
            double referenceMean = 0, enhancedMean = 0, referenceSquared = 0, enhancedSquared = 0, cross = 0;
            int pixelCount = referenceGray.Width * referenceGray.Height;
            unsafe
            {
                byte* referencePtr = (byte*)referenceGray.DataPointer;
                byte* enhancedPtr = (byte*)enhancedGray.DataPointer;
                for (int y = 0; y < referenceGray.Height; y++)
                {
                    byte* referenceRow = referencePtr + y * referenceGray.Step;
                    byte* enhancedRow = enhancedPtr + y * enhancedGray.Step;
                    for (int x = 0; x < referenceGray.Width; x++)
                    {
                        double referenceValue = referenceRow[x], enhancedValue = enhancedRow[x];
                        referenceMean += referenceValue; enhancedMean += enhancedValue;
                        referenceSquared += referenceValue * referenceValue; enhancedSquared += enhancedValue * enhancedValue;
                        cross += referenceValue * enhancedValue;
                    }
                }
            }
            referenceMean /= pixelCount; enhancedMean /= pixelCount;
            double referenceVariance = referenceSquared / pixelCount - referenceMean * referenceMean;
            double enhancedVariance = enhancedSquared / pixelCount - enhancedMean * enhancedMean;
            double covariance = cross / pixelCount - referenceMean * enhancedMean;
            const double c1 = 6.5025, c2 = 58.5225;
            return ((2 * referenceMean * enhancedMean + c1) * (2 * covariance + c2)) /
                   ((referenceMean * referenceMean + enhancedMean * enhancedMean + c1) * (referenceVariance + enhancedVariance + c2));
        }

        public static double CalculateLoe(Mat original, Mat enhanced, int resizeDimension = 50)
        {
            using Mat originalGray = ToGray(original);
            using Mat enhancedGray = ToGray(enhanced);
            EnsureSameSize(originalGray, enhancedGray);
            using Mat originalSmall = new(); using Mat enhancedSmall = new();
            CvInvoke.Resize(originalGray, originalSmall, new System.Drawing.Size(resizeDimension, resizeDimension));
            CvInvoke.Resize(enhancedGray, enhancedSmall, new System.Drawing.Size(resizeDimension, resizeDimension));
            int count = resizeDimension * resizeDimension;
            byte[] originalValues = new byte[count], enhancedValues = new byte[count];
            originalSmall.CopyTo(originalValues); enhancedSmall.CopyTo(enhancedValues);
            long errors = 0;
            for (int x = 0; x < count; x++)
                for (int y = 0; y < count; y++)
                    if ((originalValues[x] >= originalValues[y]) != (enhancedValues[x] >= enhancedValues[y])) errors++;
            return errors / (double)count;
        }

        private static void EnsureSameSize(Mat first, Mat second)
        {
            if (first.Width != second.Width || first.Height != second.Height)
                throw new ArgumentException("Ảnh tham chiếu và ảnh kết quả phải cùng kích thước.");
        }
    }
}