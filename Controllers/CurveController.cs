using Emgu.CV;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer.Controllers
{
    public sealed class CurveController
    {
        public Mat CreateLut(CurveModel model) => CurveLutService.CalculateCombinedLUTMat(model.Channels);

        public async Task<BitmapSource?> PreviewAsync(Mat original, CurveModel model)
        {
            using Mat lut = CreateLut(model);
            return await Task.Run(() =>
            {
                using Mat result = CurveLutService.ApplyLUT(original, lut);
                BitmapSource? source = ImageConversionHelper.ToBitmapSource(result);
                source?.Freeze();
                return source;
            });
        }
    }
}