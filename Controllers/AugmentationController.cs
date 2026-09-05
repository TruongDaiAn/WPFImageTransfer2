using Emgu.CV;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Services.Optimization;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Controllers
{
    public sealed class AugmentationController
    {
        public async Task<BitmapSource?> PreviewAsync(Mat original, AugmentationParameters parameters)
        {
            return await Task.Run(() =>
            {
                using Mat transformed = ImageAugmentationService.ApplyTransform(original, parameters);
                BitmapSource? source = Helpers.ImageConversionHelper.ToBitmapSource(transformed);
                source?.Freeze();
                return source;
            });
        }

        public Task<ParallelAugmentationEngine.BatchAugmentResult> GenerateBatchAsync(
            Mat original, int count, AugmentationParameters parameters)
        {
            return ParallelAugmentationEngine.GenerateParallelBatchAsync(original, count, parameters);
        }
    }
}