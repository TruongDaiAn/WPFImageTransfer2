using Emgu.CV;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Controllers
{
    public sealed class LowLightEnhancementController
    {
        public Task<LowLightEnhancementResult> ProcessAsync(LowLightEnhancementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.Run(() => Process(request));
        }

        private static LowLightEnhancementResult Process(LowLightEnhancementRequest request)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Mat enhanced = request.Algorithm switch
            {
                "GHE" => HistogramEnhancementService.ApplyGlobalHistogramEqualization(request.Original, request.ColorSpace),
                "AHE" => HistogramEnhancementService.ApplyAdaptiveHistogramEqualization(request.Original, request.GridSize, request.GridSize, request.ColorSpace),
                _ => HistogramEnhancementService.ApplyClahe(request.Original, request.ClipLimit, request.GridSize, request.GridSize, request.ColorSpace)
            };
            stopwatch.Stop();

            try
            {
                int[] histogram = HistogramEnhancementService.CalculateLuminanceHistogram(enhanced);
                ImageQualityEvaluation quality = ImageQualityEvaluationService.Evaluate(request.Original, enhanced, request.Reference);
                return new LowLightEnhancementResult(enhanced, histogram, quality, stopwatch.ElapsedMilliseconds);
            }
            catch
            {
                enhanced.Dispose();
                throw;
            }
        }
    }
}