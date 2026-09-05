using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFImageTransfer.Services;

namespace WPFImageTransfer.Controllers
{
    public sealed class FileController
    {
        public string[]? SelectImages() => ImageIoService.OpenImagesDialog();

        public bool SaveImage(BitmapSource source) => ImageIoService.SaveSingleImage(source);

        public Task ExportImagesAsync(IEnumerable<string> paths, Dictionary<string, BitmapSource> images, string folder)
            => ImageIoService.ExportAllImagesAsync(paths, images, folder);
    }
}