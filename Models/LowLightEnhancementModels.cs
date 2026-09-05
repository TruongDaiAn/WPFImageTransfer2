using Emgu.CV;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Models
{
    public sealed record LowLightEnhancementRequest(
        Mat Original,
        string Algorithm,
        double ClipLimit,
        int GridSize,
        HistogramEnhancementService.EnhancementColorSpace ColorSpace,
        Mat? Reference = null);

    public sealed record LowLightEnhancementResult(
        Mat Enhanced,
        int[] EnhancedHistogram,
        ImageQualityEvaluation Quality,
        long ProcessingTimeMilliseconds);
}