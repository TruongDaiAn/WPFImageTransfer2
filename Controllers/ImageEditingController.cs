using Emgu.CV;
using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Services;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Controllers
{
    public sealed class ImageEditingController
    {
        private readonly ImageCacheManager _cache;

        public ImageEditingController(ImageCacheManager cache)
        {
            _cache = cache;
        }

        public async Task<(BitmapSource? Source, Mat Mat)> ApplyAsync(
            string path, Func<Mat, Mat> operation)
        {
            return await Task.Run(() =>
            {
                using Mat sourceMat = _cache.GetOrLoadMat(path);
                Mat resultMat = operation(sourceMat);
                BitmapSource? source = ImageConversionHelper.ToBitmapSource(resultMat);
                source?.Freeze();
                return (source, resultMat);
            });
        }

        public Task<(BitmapSource? Source, Mat Mat)> ApplyMovableMaskAsync(
            string path, int centerX, int centerY, int radius, bool isCircle)
        {
            return ApplyAsync(path, mat => ImageMaskingService.ApplyMovableMaskCrop(mat, centerX, centerY, radius, isCircle));
        }

        public Task<(BitmapSource? Source, Mat Mat)> ApplySelectionAsync(
            string path, System.Drawing.Rectangle cropRect, bool isEllipse, bool isObjectSelection)
        {
            return ApplyAsync(path, mat => ImageMaskingService.ApplySelectionCrop(mat, cropRect, isEllipse, isObjectSelection));
        }

        public Task<(BitmapSource? Source, Mat Mat)> ApplyImageMaskAsync(string path, string maskPath)
        {
            return ApplyAsync(path, mat => ImageMaskingService.ApplyImageAlphaMask(mat, maskPath));
        }
    }
}