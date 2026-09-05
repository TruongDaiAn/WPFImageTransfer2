using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer.Controllers
{
    public sealed class PreviewController
    {
        public PreviewWindow CreatePreviewWindow(ImageSource source)
            => new PreviewWindow(source);

        public PreviewAllWindow CreatePreviewAllWindow(IEnumerable<BitmapSource> sources)
            => new PreviewAllWindow(new List<BitmapSource>(sources));
    }
}