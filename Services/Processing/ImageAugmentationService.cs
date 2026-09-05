using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WPFImageTransfer.Services.Processing
{
    public class AugmentationParameters
    {
        public bool FlipH { get; set; }
        public bool FlipV { get; set; }
        public double RotateAngle { get; set; }
        public double ShearAngle { get; set; }
        public double ShiftXPercent { get; set; }
        public double ShiftYPercent { get; set; }
    }

    /// <summary>
    /// Cung cấp các phép biến đổi hình học (Affine Transformation) và sinh dữ liệu tăng cường (Data Augmentation).
    /// </summary>
    public static class ImageAugmentationService
    {
        public static Mat ApplyTransform(Mat originalMat, AugmentationParameters param)
        {
            return ApplyTransform(originalMat, param.FlipH, param.FlipV, param.RotateAngle, param.ShearAngle, param.ShiftXPercent, param.ShiftYPercent);
        }

        public static Mat ApplyTransform(Mat originalMat, bool flipH, bool flipV, double rotateAngle, double shearAngle, double shiftXPercent, double shiftYPercent)
        {
            Mat bgraMat = new Mat();
            if (originalMat.NumberOfChannels == 3)
                CvInvoke.CvtColor(originalMat, bgraMat, ColorConversion.Bgr2Bgra);
            else
                originalMat.CopyTo(bgraMat);

            int width = bgraMat.Width;
            int height = bgraMat.Height;

            // 1. Flip
            if (flipH || flipV)
            {
                Mat flipped = new Mat();
                if (flipH && flipV) CvInvoke.Flip(bgraMat, flipped, FlipType.Both);
                else if (flipH) CvInvoke.Flip(bgraMat, flipped, FlipType.Horizontal);
                else CvInvoke.Flip(bgraMat, flipped, FlipType.Vertical);
                bgraMat.Dispose();
                bgraMat = flipped;
            }

            // 2. Rotate với mở rộng khung hình bảo toàn toàn bộ ảnh
            if (rotateAngle != 0)
            {
                double angleRad = rotateAngle * Math.PI / 180.0;
                double cosA = Math.Abs(Math.Cos(angleRad));
                double sinA = Math.Abs(Math.Sin(angleRad));
                int newWidth = (int)Math.Round(width * cosA + height * sinA);
                int newHeight = (int)Math.Round(width * sinA + height * cosA);

                PointF center = new PointF(width / 2f, height / 2f);
                using (Mat rotMat = new Mat())
                {
                    CvInvoke.GetRotationMatrix2D(center, rotateAngle, 1.0, rotMat);

                    double[] rotData = new double[6];
                    Marshal.Copy(rotMat.DataPointer, rotData, 0, 6);
                    rotData[2] += (newWidth / 2.0) - center.X;
                    rotData[5] += (newHeight / 2.0) - center.Y;
                    Marshal.Copy(rotData, 0, rotMat.DataPointer, 6);

                    Mat rotated = new Mat();
                    CvInvoke.WarpAffine(bgraMat, rotated, rotMat, new Size(newWidth, newHeight), Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));
                    bgraMat.Dispose();
                    bgraMat = rotated;
                    width = newWidth;
                    height = newHeight;
                }
            }

            // 3. Shear (Nghiêng hình học)
            if (shearAngle != 0)
            {
                double shearRad = shearAngle * Math.PI / 180.0;
                double sh_x = Math.Tan(shearRad);

                int newWidth = width + (int)Math.Abs(height * sh_x);
                int newHeight = height;
                float tx = sh_x < 0 ? (float)Math.Abs(height * sh_x) : 0;

                float[] warpValues = new float[] { 1, (float)sh_x, tx, 0, 1, 0 };
                using (Mat shearMat = new Mat(2, 3, DepthType.Cv32F, 1))
                {
                    Marshal.Copy(warpValues, 0, shearMat.DataPointer, 6);
                    Mat sheared = new Mat();
                    CvInvoke.WarpAffine(bgraMat, sheared, shearMat, new Size(newWidth, newHeight), Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));
                    bgraMat.Dispose();
                    bgraMat = sheared;
                    width = newWidth;
                    height = newHeight;
                }
            }

            // 4. Shift (Dịch chuyển)
            if (shiftXPercent != 0 || shiftYPercent != 0)
            {
                double shiftX = (shiftXPercent / 100.0) * width;
                double shiftY = (shiftYPercent / 100.0) * height;

                int newWidth = width + (int)Math.Abs(shiftX);
                int newHeight = height + (int)Math.Abs(shiftY);

                float tx = shiftX > 0 ? (float)shiftX : 0;
                float ty = shiftY > 0 ? (float)shiftY : 0;

                float[] warpValues = new float[] { 1, 0, tx, 0, 1, ty };
                using (Mat shiftMat = new Mat(2, 3, DepthType.Cv32F, 1))
                {
                    Marshal.Copy(warpValues, 0, shiftMat.DataPointer, 6);
                    Mat shifted = new Mat();
                    CvInvoke.WarpAffine(bgraMat, shifted, shiftMat, new Size(newWidth, newHeight), Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));
                    bgraMat.Dispose();
                    bgraMat = shifted;
                }
            }

            return bgraMat;
        }

        public static List<Mat> GenerateBatchRandom(Mat originalMat, int count, AugmentationParameters param)
        {
            List<Mat> results = new List<Mat>();
            Random rand = new Random();

            for (int i = 0; i < count; i++)
            {
                bool currentFlipH = param.FlipH && (rand.Next(2) == 0);
                bool currentFlipV = param.FlipV && (rand.Next(2) == 0);

                double currentRotateAngle = 0;
                if (param.RotateAngle != 0)
                {
                    double minA = Math.Min(-param.RotateAngle, param.RotateAngle);
                    double maxA = Math.Max(-param.RotateAngle, param.RotateAngle);
                    currentRotateAngle = minA + rand.NextDouble() * (maxA - minA);
                }

                double currentShearAngle = 0;
                if (param.ShearAngle != 0)
                {
                    double minS = Math.Min(-param.ShearAngle, param.ShearAngle);
                    double maxS = Math.Max(-param.ShearAngle, param.ShearAngle);
                    currentShearAngle = minS + rand.NextDouble() * (maxS - minS);
                }

                double currentShiftX = 0;
                if (param.ShiftXPercent != 0)
                {
                    double minX = Math.Min(-param.ShiftXPercent, param.ShiftXPercent);
                    double maxX = Math.Max(-param.ShiftXPercent, param.ShiftXPercent);
                    currentShiftX = minX + rand.NextDouble() * (maxX - minX);
                }

                double currentShiftY = 0;
                if (param.ShiftYPercent != 0)
                {
                    double minY = Math.Min(-param.ShiftYPercent, param.ShiftYPercent);
                    double maxY = Math.Max(-param.ShiftYPercent, param.ShiftYPercent);
                    currentShiftY = minY + rand.NextDouble() * (maxY - minY);
                }

                Mat augmented = ApplyTransform(originalMat, currentFlipH, currentFlipV, currentRotateAngle, currentShearAngle, currentShiftX, currentShiftY);
                results.Add(augmented);
            }

            return results;
        }
    }
}
