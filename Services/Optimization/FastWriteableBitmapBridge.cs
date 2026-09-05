using Emgu.CV;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer.Services.Optimization
{
    /// <summary>
    /// Module tối ưu hóa: Cầu nối bộ nhớ Zero-Copy (Fast WriteableBitmap Bridge)
    /// Ghi đè trực tiếp dữ liệu pixel từ OpenCV vào GPU Texture buffer của WPF không cần tạo mới BitmapSource.
    /// </summary>
    public class FastWriteableBitmapBridge
    {
        private WriteableBitmap? _reusableBitmap;

        public WriteableBitmap? CurrentBitmap => _reusableBitmap;

        public WriteableBitmap UpdateFromMat(Mat mat)
        {
            if (mat == null || mat.IsEmpty) throw new ArgumentNullException(nameof(mat));

            int width = mat.Width;
            int height = mat.Height;
            int stride = mat.Step;

            PixelFormat format = mat.NumberOfChannels == 1 ? PixelFormats.Gray8 :
                                 mat.NumberOfChannels == 3 ? PixelFormats.Bgr24 : PixelFormats.Bgra32;

            if (_reusableBitmap == null || 
                _reusableBitmap.PixelWidth != width || 
                _reusableBitmap.PixelHeight != height || 
                _reusableBitmap.Format != format)
            {
                _reusableBitmap = new WriteableBitmap(width, height, 96, 96, format, null);
            }

            _reusableBitmap.Lock();
            try
            {
                int backBufferStride = _reusableBitmap.BackBufferStride;
                unsafe
                {
                    if (stride == backBufferStride)
                    {
                        Buffer.MemoryCopy(
                            (void*)mat.DataPointer, 
                            (void*)_reusableBitmap.BackBuffer, 
                            (long)stride * height, 
                            (long)stride * height);
                    }
                    else
                    {
                        int copyBytesPerRow = Math.Min(stride, backBufferStride);
                        byte* src = (byte*)mat.DataPointer;
                        byte* dst = (byte*)_reusableBitmap.BackBuffer;
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(src + (y * stride), dst + (y * backBufferStride), copyBytesPerRow, copyBytesPerRow);
                        }
                    }
                }

                _reusableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _reusableBitmap.Unlock();
            }

            return _reusableBitmap;
        }

        public void Reset()
        {
            _reusableBitmap = null;
        }
    }
}
