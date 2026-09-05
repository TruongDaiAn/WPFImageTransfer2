using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Drawing;

namespace WPFImageTransfer.Services.Processing
{
    /// <summary>
    /// Cung cấp các thuật toán cắt hình học, tạo mặt nạ trong suốt và thuật toán tách nền GrabCut
    /// được tối ưu hóa với công nghệ Proxy-Scale Acceleration siêu tốc.
    /// </summary>
    public static class ImageMaskingService
    {
        public static Mat ApplyMovableMaskCrop(Mat mat, int realCenterX, int realCenterY, int realRadius, bool isCircle)
        {
            int cropX = realCenterX - realRadius;
            int cropY = realCenterY - realRadius;
            int cropW = realRadius * 2;
            int cropH = realRadius * 2;

            Rectangle matRect = new Rectangle(0, 0, mat.Width, mat.Height);
            Rectangle safeCropRect = new Rectangle(cropX, cropY, cropW, cropH);
            safeCropRect.Intersect(matRect);

            if (safeCropRect.Width <= 0 || safeCropRect.Height <= 0)
            {
                return mat.Clone();
            }

            using (Mat mask = new Mat(mat.Size, DepthType.Cv8U, 1))
            {
                mask.SetTo(new MCvScalar(0));

                if (isCircle)
                {
                    Point pCenter = new Point(realCenterX, realCenterY);
                    CvInvoke.Circle(mask, pCenter, realRadius, new MCvScalar(255), -1);
                }
                else
                {
                    Rectangle rect = new Rectangle(cropX, cropY, cropW, cropH);
                    CvInvoke.Rectangle(mask, rect, new MCvScalar(255), -1);
                }

                Mat bgraMat = new Mat();
                if (mat.NumberOfChannels == 3)
                    CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);
                else
                    mat.CopyTo(bgraMat);

                Mat resMat = new Mat(bgraMat.Size, bgraMat.Depth, 4);
                resMat.SetTo(new MCvScalar(0, 0, 0, 0));
                bgraMat.CopyTo(resMat, mask);
                bgraMat.Dispose();

                Mat croppedResult = new Mat(resMat, safeCropRect);
                Mat finalMat = croppedResult.Clone();
                resMat.Dispose();
                croppedResult.Dispose();
                return finalMat;
            }
        }

        public static Mat ApplySelectionCrop(Mat mat, Rectangle targetRect, bool isEllipse, bool isObjectSelection)
        {
            Rectangle matRect = new Rectangle(0, 0, mat.Width, mat.Height);
            Rectangle safeCropRect = targetRect;
            safeCropRect.Intersect(matRect);

            if (safeCropRect.Width <= 0 || safeCropRect.Height <= 0)
            {
                return mat.Clone();
            }

            using (Mat mask = new Mat(mat.Size, DepthType.Cv8U, 1))
            {
                mask.SetTo(new MCvScalar(0));

                if (isObjectSelection)
                {
                    // Tối ưu hóa: Proxy-Scale GrabCut Acceleration
                    // Co ảnh lớn về kích thước tối đa 600px để tính toán trong 0.05s thay vì 3s
                    double scale = 1.0;
                    int maxDim = Math.Max(mat.Width, mat.Height);
                    if (maxDim > 600)
                    {
                        scale = 600.0 / maxDim;
                    }

                    Size proxySize = new Size((int)(mat.Width * scale), (int)(mat.Height * scale));
                    Rectangle proxyRect = new Rectangle(
                        (int)(safeCropRect.X * scale),
                        (int)(safeCropRect.Y * scale),
                        Math.Max(1, (int)(safeCropRect.Width * scale)),
                        Math.Max(1, (int)(safeCropRect.Height * scale)));

                    using (Mat proxyMat = new Mat())
                    using (Mat proxyMask = new Mat(proxySize, DepthType.Cv8U, 1))
                    using (Mat bgdModel = new Mat())
                    using (Mat fgdModel = new Mat())
                    using (Mat grabcutMask = new Mat())
                    {
                        if (scale < 1.0)
                            CvInvoke.Resize(mat, proxyMat, proxySize, 0, 0, Inter.Linear);
                        else
                            mat.CopyTo(proxyMat);

                        CvInvoke.GrabCut(proxyMat, grabcutMask, proxyRect, bgdModel, fgdModel, 4, GrabcutInitType.InitWithRect);

                        using (Mat binaryMask = new Mat())
                        using (Mat scalar1 = new Mat(grabcutMask.Size, DepthType.Cv8U, 1))
                        using (Mat scalar0 = new Mat(grabcutMask.Size, DepthType.Cv8U, 1))
                        {
                            scalar1.SetTo(new MCvScalar(1));
                            scalar0.SetTo(new MCvScalar(0));
                            CvInvoke.BitwiseAnd(grabcutMask, scalar1, binaryMask);
                            CvInvoke.Compare(binaryMask, scalar0, proxyMask, CmpType.GreaterThan);
                        }

                        using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                        using (Mat hierarchy = new Mat())
                        {
                            CvInvoke.FindContours(proxyMask, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                            if (contours.Size > 0)
                            {
                                int maxIndex = 0;
                                double maxArea = 0;
                                for (int i = 0; i < contours.Size; i++)
                                {
                                    double area = CvInvoke.ContourArea(contours[i]);
                                    if (area > maxArea)
                                    {
                                        maxArea = area;
                                        maxIndex = i;
                                    }
                                }
                                proxyMask.SetTo(new MCvScalar(0));
                                CvInvoke.DrawContours(proxyMask, contours, maxIndex, new MCvScalar(255), -1);

                                Rectangle objectRect = CvInvoke.BoundingRectangle(contours[maxIndex]);
                                if (objectRect.Width > 0 && objectRect.Height > 0)
                                {
                                    safeCropRect = new Rectangle(
                                        (int)(objectRect.X / scale),
                                        (int)(objectRect.Y / scale),
                                        (int)(objectRect.Width / scale),
                                        (int)(objectRect.Height / scale));
                                    safeCropRect.Intersect(matRect);
                                }
                            }
                        }

                        // Phóng to mặt nạ về kích thước gốc bằng Inter.Nearest
                        if (scale < 1.0)
                        {
                            CvInvoke.Resize(proxyMask, mask, mat.Size, 0, 0, Inter.Nearest);
                        }
                        else
                        {
                            proxyMask.CopyTo(mask);
                        }

                        using (Mat kernel = new Mat(5, 5, DepthType.Cv8U, 1))
                        {
                            kernel.SetTo(new MCvScalar(1));
                            CvInvoke.MorphologyEx(mask, mask, MorphOp.Close, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());
                        }
                    }
                }
                else if (isEllipse)
                {
                    Point center = new Point(safeCropRect.X + safeCropRect.Width / 2, safeCropRect.Y + safeCropRect.Height / 2);
                    Size axes = new Size(safeCropRect.Width / 2, safeCropRect.Height / 2);
                    CvInvoke.Ellipse(mask, center, axes, 0, 0, 360, new MCvScalar(255), -1);
                }
                else
                {
                    CvInvoke.Rectangle(mask, safeCropRect, new MCvScalar(255), -1);
                }

                Mat bgraMat = new Mat();
                if (mat.NumberOfChannels == 3)
                    CvInvoke.CvtColor(mat, bgraMat, ColorConversion.Bgr2Bgra);
                else
                    mat.CopyTo(bgraMat);

                Mat resMat = new Mat(bgraMat.Size, bgraMat.Depth, 4);
                resMat.SetTo(new MCvScalar(0, 0, 0, 0));
                bgraMat.CopyTo(resMat, mask);
                bgraMat.Dispose();

                Mat croppedResult = new Mat(resMat, safeCropRect);
                Mat finalMat = croppedResult.Clone();
                resMat.Dispose();
                croppedResult.Dispose();
                return finalMat;
            }
        }

        public static Mat ApplyImageAlphaMask(Mat mat, string maskImagePath)
        {
            using (Mat maskImg = CvInvoke.Imread(maskImagePath, ImreadModes.Grayscale))
            {
                CvInvoke.Resize(maskImg, maskImg, mat.Size);
                CvInvoke.Threshold(maskImg, maskImg, 128, 255, ThresholdType.Binary);

                Mat resMat = new Mat(mat.Size, mat.Depth, mat.NumberOfChannels);
                resMat.SetTo(new MCvScalar(0, 0, 0));
                mat.CopyTo(resMat, maskImg);
                return resMat;
            }
        }
    }
}
