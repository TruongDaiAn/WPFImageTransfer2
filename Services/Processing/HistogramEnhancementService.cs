using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Threading.Tasks;

namespace WPFImageTransfer.Services.Processing
{
    /// <summary>
    /// Cung cấp các thuật toán tăng cường ảnh thiếu sáng viết thuần từ đầu (From Scratch):
    /// 1. Global Histogram Equalization (GHE) - Cân bằng biểu đồ toàn cục trên không gian màu YCrCb.
    /// 2. Adaptive Histogram Equalization (AHE) - Cân bằng biểu đồ thích nghi cục bộ theo ô lưới.
    /// 3. Contrast Limited Adaptive Histogram Equalization (CLAHE) - Cân bằng thích nghi giới hạn tương phản chống nổ nhiễu (Karel Zuiderveld).
    /// </summary>
    public static class HistogramEnhancementService
    {
        public enum EnhancementColorSpace
        {
            YCrCb,
            Lab,
            Grayscale
        }

        /// <summary>
        /// 1. Thuật toán cân bằng biểu đồ toàn cục (Global Histogram Equalization - GHE) thuần C#.
        /// Kéo dãn dải tương phản của toàn bộ ảnh về phân bố đều [0, 255].
        /// </summary>
        public static Mat ApplyGlobalHistogramEqualization(Mat inputMat, EnhancementColorSpace colorSpace = EnhancementColorSpace.YCrCb)
        {
            if (inputMat == null || inputMat.IsEmpty) return new Mat();

            if (inputMat.NumberOfChannels == 1 || colorSpace == EnhancementColorSpace.Grayscale)
            {
                Mat grayMat = new Mat();
                if (inputMat.NumberOfChannels > 1)
                    CvInvoke.CvtColor(inputMat, grayMat, ColorConversion.Bgr2Gray);
                else
                    grayMat = inputMat.Clone();

                Mat equalized = ProcessGrayscaleGhe(grayMat);
                grayMat.Dispose();
                return equalized;
            }

            // Chuyển sang không gian màu để bảo toàn sắc độ màu
            ColorConversion toColorSpace = colorSpace == EnhancementColorSpace.Lab ? ColorConversion.Bgr2Lab : ColorConversion.Bgr2YCrCb;
            ColorConversion toBgr = colorSpace == EnhancementColorSpace.Lab ? ColorConversion.Lab2Bgr : ColorConversion.YCrCb2Bgr;

            Mat convertedMat = new Mat();
            CvInvoke.CvtColor(inputMat, convertedMat, toColorSpace);

            Mat[] channels = convertedMat.Split();
            Mat lumaChannel = channels[0]; // Kênh Y (trong YCrCb) hoặc Kênh L (trong Lab)

            Mat equalizedLuma = ProcessGrayscaleGhe(lumaChannel);
            channels[0].Dispose();
            channels[0] = equalizedLuma;

            using (VectorOfMat vec = new VectorOfMat(channels))
            {
                CvInvoke.Merge(vec, convertedMat);
            }

            Mat resultMat = new Mat();
            CvInvoke.CvtColor(convertedMat, resultMat, toBgr);

            foreach (var ch in channels) ch.Dispose();
            convertedMat.Dispose();

            return resultMat;
        }

        /// <summary>
        /// 2. Thuật toán cân bằng biểu đồ thích nghi cục bộ (Adaptive Histogram Equalization - AHE) thuần C#.
        /// Chia lưới ô ngữ cảnh và nội suy song tuyến tính (Bilinear Interpolation) không giới hạn clip.
        /// </summary>
        public static Mat ApplyAdaptiveHistogramEqualization(Mat inputMat, int gridX = 8, int gridY = 8, EnhancementColorSpace colorSpace = EnhancementColorSpace.YCrCb)
        {
            // AHE là trường hợp đặc biệt của CLAHE với clipLimit = 0 (không cắt ngưỡng)
            return ApplyClahe(inputMat, clipLimit: 0, gridX: gridX, gridY: gridY, colorSpace: colorSpace);
        }

        /// <summary>
        /// 3. Thuật toán CLAHE (Contrast Limited Adaptive Histogram Equalization - Karel Zuiderveld 1994) thuần C#.
        /// Giới hạn tương phản chống nổ nhiễu ở vùng đồng nhất và nội suy song tuyến tính mượt mà 100%.
        /// </summary>
        public static Mat ApplyClahe(Mat inputMat, double clipLimit = 2.0, int gridX = 8, int gridY = 8, EnhancementColorSpace colorSpace = EnhancementColorSpace.YCrCb)
        {
            if (inputMat == null || inputMat.IsEmpty) return new Mat();

            gridX = Math.Clamp(gridX, 2, 64);
            gridY = Math.Clamp(gridY, 2, 64);

            if (inputMat.NumberOfChannels == 1 || colorSpace == EnhancementColorSpace.Grayscale)
            {
                Mat grayMat = new Mat();
                if (inputMat.NumberOfChannels > 1)
                    CvInvoke.CvtColor(inputMat, grayMat, ColorConversion.Bgr2Gray);
                else
                    grayMat = inputMat.Clone();

                Mat equalized = ProcessGrayscaleClahe(grayMat, clipLimit, gridX, gridY);
                grayMat.Dispose();
                return equalized;
            }

            ColorConversion toColorSpace = colorSpace == EnhancementColorSpace.Lab ? ColorConversion.Bgr2Lab : ColorConversion.Bgr2YCrCb;
            ColorConversion toBgr = colorSpace == EnhancementColorSpace.Lab ? ColorConversion.Lab2Bgr : ColorConversion.YCrCb2Bgr;

            Mat convertedMat = new Mat();
            CvInvoke.CvtColor(inputMat, convertedMat, toColorSpace);

            Mat[] channels = convertedMat.Split();
            Mat lumaChannel = channels[0];

            Mat equalizedLuma = ProcessGrayscaleClahe(lumaChannel, clipLimit, gridX, gridY);
            channels[0].Dispose();
            channels[0] = equalizedLuma;

            using (VectorOfMat vec = new VectorOfMat(channels))
            {
                CvInvoke.Merge(vec, convertedMat);
            }

            Mat resultMat = new Mat();
            CvInvoke.CvtColor(convertedMat, resultMat, toBgr);

            foreach (var ch in channels) ch.Dispose();
            convertedMat.Dispose();

            return resultMat;
        }

        /// <summary>
        /// Trích xuất biểu đồ phân bố độ sáng 256 bin của ảnh (Luminance Histogram) để vẽ biểu đồ trực quan.
        /// </summary>
        public static unsafe int[] CalculateLuminanceHistogram(Mat mat)
        {
            int[] histogram = new int[256];
            if (mat == null || mat.IsEmpty) return histogram;

            Mat grayMat = new Mat();
            if (mat.NumberOfChannels == 3)
            {
                CvInvoke.CvtColor(mat, grayMat, ColorConversion.Bgr2Gray);
            }
            else
            {
                grayMat = mat.Clone();
            }

            int width = grayMat.Width;
            int height = grayMat.Height;
            int step = grayMat.Step;
            byte* ptr = (byte*)grayMat.DataPointer;

            for (int y = 0; y < height; y++)
            {
                byte* row = ptr + (y * step);
                for (int x = 0; x < width; x++)
                {
                    histogram[row[x]]++;
                }
            }

            grayMat.Dispose();
            return histogram;
        }

        #region Core Pure C# Algorithms (From Scratch)

        /// <summary>
        /// Thuật toán GHE thuần C# trên 1 kênh mức xám (Grayscale / Luminance).
        /// </summary>
        private static unsafe Mat ProcessGrayscaleGhe(Mat grayMat)
        {
            int width = grayMat.Width;
            int height = grayMat.Height;
            int step = grayMat.Step;
            int totalPixels = width * height;

            if (totalPixels == 0) return grayMat.Clone();

            // 1. Tính toán Histogram 256 bins
            int[] hist = new int[256];
            byte* srcPtr = (byte*)grayMat.DataPointer;

            for (int y = 0; y < height; y++)
            {
                byte* row = srcPtr + (y * step);
                for (int x = 0; x < width; x++)
                {
                    hist[row[x]]++;
                }
            }

            // 2. Tính hàm phân bố tích lũy CDF (Cumulative Distribution Function)
            int[] cdf = new int[256];
            int sum = 0;
            int cdfMin = -1;

            for (int i = 0; i < 256; i++)
            {
                sum += hist[i];
                cdf[i] = sum;
                if (cdfMin == -1 && cdf[i] > 0)
                {
                    cdfMin = cdf[i];
                }
            }

            // 3. Tính bảng ánh xạ 1D LUT chuẩn hóa [0, 255]
            byte[] lut = new byte[256];
            int denominator = totalPixels - cdfMin;
            if (denominator <= 0) denominator = 1;

            for (int i = 0; i < 256; i++)
            {
                if (cdf[i] <= cdfMin)
                {
                    lut[i] = 0;
                }
                else
                {
                    double normalized = ((double)(cdf[i] - cdfMin) / denominator) * 255.0;
                    lut[i] = (byte)Math.Clamp((int)Math.Round(normalized), 0, 255);
                }
            }

            // 4. Ánh xạ dữ liệu sang ảnh đầu ra
            Mat result = new Mat(height, width, DepthType.Cv8U, 1);
            byte* dstPtr = (byte*)result.DataPointer;
            int dstStep = result.Step;

            Parallel.For(0, height, y =>
            {
                byte* srcRow = srcPtr + (y * step);
                byte* dstRow = dstPtr + (y * dstStep);
                for (int x = 0; x < width; x++)
                {
                    dstRow[x] = lut[srcRow[x]];
                }
            });

            return result;
        }

        /// <summary>
        /// Thuật toán CLAHE thuần C# theo bài báo của Karel Zuiderveld (1994).
        /// Kết hợp Clip-Limit Redistribution và Bilinear Interpolation.
        /// </summary>
        private static unsafe Mat ProcessGrayscaleClahe(Mat grayMat, double clipLimit, int gridX, int gridY)
        {
            int width = grayMat.Width;
            int height = grayMat.Height;
            int step = grayMat.Step;

            // Bảng LUT cho từng ô lưới: [gridY, gridX, 256]
            byte[,,] tileLuts = new byte[gridY, gridX, 256];

            int[] tileStartX = new int[gridX];
            int[] tileEndX = new int[gridX];
            double[] tileCenterX = new double[gridX];

            for (int gx = 0; gx < gridX; gx++)
            {
                tileStartX[gx] = gx * width / gridX;
                tileEndX[gx] = (gx + 1) * width / gridX;
                tileCenterX[gx] = (tileStartX[gx] + tileEndX[gx] - 1) / 2.0;
            }

            int[] tileStartY = new int[gridY];
            int[] tileEndY = new int[gridY];
            double[] tileCenterY = new double[gridY];

            for (int gy = 0; gy < gridY; gy++)
            {
                tileStartY[gy] = gy * height / gridY;
                tileEndY[gy] = (gy + 1) * height / gridY;
                tileCenterY[gy] = (tileStartY[gy] + tileEndY[gy] - 1) / 2.0;
            }

            byte* srcPtr = (byte*)grayMat.DataPointer;

            // 1. Tính toán Histogram, Cắt ngưỡng và tạo CDF LUT cho từng ô lưới
            Parallel.For(0, gridY, gy =>
            {
                int y0 = tileStartY[gy];
                int y1 = tileEndY[gy];

                for (int gx = 0; gx < gridX; gx++)
                {
                    int x0 = tileStartX[gx];
                    int x1 = tileEndX[gx];
                    int numPixelsInTile = (y1 - y0) * (x1 - x0);

                    // a. Tính Local Histogram
                    int[] hist = new int[256];
                    for (int y = y0; y < y1; y++)
                    {
                        byte* row = srcPtr + (y * step);
                        for (int x = x0; x < x1; x++)
                        {
                            hist[row[x]]++;
                        }
                    }

                    // b. Cắt ngưỡng bão hòa (Clip Limit) & Phân phối phần dư (Redistribution)
                    if (clipLimit > 0)
                    {
                        int actualClip = (int)Math.Max(1, Math.Round(clipLimit * numPixelsInTile / 256.0));
                        int excess = 0;

                        for (int i = 0; i < 256; i++)
                        {
                            if (hist[i] > actualClip)
                            {
                                excess += (hist[i] - actualClip);
                                hist[i] = actualClip;
                            }
                        }

                        int bonus = excess / 256;
                        int remainder = excess % 256;

                        for (int i = 0; i < 256; i++)
                        {
                            hist[i] += bonus;
                            if (i < remainder) hist[i]++;
                        }
                    }

                    // c. Tính Cumulative Distribution Function (CDF) và bảng biến đổi Mapping LUT
                    int sum = 0;
                    double scale = 255.0 / numPixelsInTile;

                    for (int i = 0; i < 256; i++)
                    {
                        sum += hist[i];
                        int mappedVal = (int)Math.Round(sum * scale);
                        tileLuts[gy, gx, i] = (byte)Math.Clamp(mappedVal, 0, 255);
                    }
                }
            });

            // 2. Nội suy song tuyến tính (Bilinear Interpolation) cho từng điểm ảnh
            Mat result = new Mat(height, width, DepthType.Cv8U, 1);
            byte* dstPtr = (byte*)result.DataPointer;
            int dstStep = result.Step;

            Parallel.For(0, height, y =>
            {
                byte* srcRow = srcPtr + (y * step);
                byte* dstRow = dstPtr + (y * dstStep);

                // Xác định 2 chỉ số ô theo trục Y (y1, y2) và trọng số nội suy t
                int gy1, gy2;
                double t;

                if (y <= tileCenterY[0])
                {
                    gy1 = 0;
                    gy2 = 0;
                    t = 0;
                }
                else if (y >= tileCenterY[gridY - 1])
                {
                    gy1 = gridY - 1;
                    gy2 = gridY - 1;
                    t = 0;
                }
                else
                {
                    int gy = 0;
                    while (gy < gridY - 1 && !(tileCenterY[gy] <= y && y < tileCenterY[gy + 1]))
                    {
                        gy++;
                    }
                    gy1 = gy;
                    gy2 = gy + 1;
                    t = (y - tileCenterY[gy1]) / (tileCenterY[gy2] - tileCenterY[gy1]);
                }

                for (int x = 0; x < width; x++)
                {
                    byte pixelVal = srcRow[x];

                    // Xác định 2 chỉ số ô theo trục X (x1, x2) và trọng số nội suy s
                    int gx1, gx2;
                    double s;

                    if (x <= tileCenterX[0])
                    {
                        gx1 = 0;
                        gx2 = 0;
                        s = 0;
                    }
                    else if (x >= tileCenterX[gridX - 1])
                    {
                        gx1 = gridX - 1;
                        gx2 = gridX - 1;
                        s = 0;
                    }
                    else
                    {
                        int gx = 0;
                        while (gx < gridX - 1 && !(tileCenterX[gx] <= x && x < tileCenterX[gx + 1]))
                        {
                            gx++;
                        }
                        gx1 = gx;
                        gx2 = gx + 1;
                        s = (x - tileCenterX[gx1]) / (tileCenterX[gx2] - tileCenterX[gx1]);
                    }

                    // Lấy 4 giá trị từ 4 bảng LUT của 4 ô lân cận
                    byte valTL = tileLuts[gy1, gx1, pixelVal];
                    byte valTR = tileLuts[gy1, gx2, pixelVal];
                    byte valBL = tileLuts[gy2, gx1, pixelVal];
                    byte valBR = tileLuts[gy2, gx2, pixelVal];

                    // Công thức nội suy song tuyến tính 2D (Bilinear Interpolation)
                    double interpolated = (1.0 - s) * (1.0 - t) * valTL +
                                          s * (1.0 - t) * valTR +
                                          (1.0 - s) * t * valBL +
                                          s * t * valBR;

                    dstRow[x] = (byte)Math.Clamp((int)Math.Round(interpolated), 0, 255);
                }
            });

            return result;
        }

        #endregion
    }
}
