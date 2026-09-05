using Emgu.CV;
using Emgu.CV.CvEnum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace WPFImageTransfer.Services.Processing
{
    /// <summary>
    /// Cung cấp thuật toán nội suy Catmull-Rom và tạo bảng tra cứu màu sắc (Look-Up Table - LUT).
    /// </summary>
    public static class CurveLutService
    {
        public static List<Point> GenerateSmoothCurve(List<Point> pts)
        {
            List<Point> result = new List<Point>();
            if (pts.Count < 2) return result;

            List<Point> padded = new List<Point>
            {
                new Point(pts[0].X, pts[0].Y)
            };
            padded.AddRange(pts);
            padded.Add(new Point(pts[pts.Count - 1].X, pts[pts.Count - 1].Y));

            for (int i = 1; i < padded.Count - 2; i++)
            {
                Point p0 = padded[i - 1];
                Point p1 = padded[i];
                Point p2 = padded[i + 1];
                Point p3 = padded[i + 2];

                for (double t = 0; t <= 1.0; t += 0.05)
                {
                    double x = p1.X + (p2.X - p1.X) * t;
                    double y = CatmullRom(t, p0.Y, p1.Y, p2.Y, p3.Y);
                    result.Add(new Point(Math.Max(0, Math.Min(255, x)), Math.Max(0, Math.Min(255, y))));
                }
            }

            return result.OrderBy(p => p.X).ToList();
        }

        private static double CatmullRom(double t, double p0, double p1, double p2, double p3)
        {
            return 0.5 * (
                (2 * p1) +
                (-p0 + p2) * t +
                (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t
            );
        }

        public static byte[] CalculateSingleLUT(List<Point> points)
        {
            byte[] lut = new byte[256];
            var curve = GenerateSmoothCurve(points);

            for (int i = 0; i < 256; i++)
            {
                double bestDist = double.MaxValue;
                double bestY = 0;
                foreach (var p in curve)
                {
                    double dist = Math.Abs(p.X - i);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestY = p.Y;
                    }
                }
                double val = 255 - bestY;
                lut[i] = (byte)Math.Max(0, Math.Min(255, val));
            }
            return lut;
        }

        public static Mat CalculateCombinedLUTMat(Dictionary<string, List<Point>> channels)
        {
            byte[] masterLUT = CalculateSingleLUT(channels["RGB"]);
            byte[] rLUT = CalculateSingleLUT(channels["Red"]);
            byte[] gLUT = CalculateSingleLUT(channels["Green"]);
            byte[] bLUT = CalculateSingleLUT(channels["Blue"]);

            byte[] combined = new byte[768];
            for (int i = 0; i < 256; i++)
            {
                // BGR byte order for OpenCV 3-channel LUT
                combined[i * 3 + 0] = masterLUT[bLUT[i]];
                combined[i * 3 + 1] = masterLUT[gLUT[i]];
                combined[i * 3 + 2] = masterLUT[rLUT[i]];
            }

            Mat lutMat = new Mat(1, 256, DepthType.Cv8U, 3);
            Marshal.Copy(combined, 0, lutMat.DataPointer, 768);
            return lutMat;
        }

        public static Mat ApplyLUT(Mat input, Mat lutMat)
        {
            Mat dest = new Mat();
            CvInvoke.LUT(input, lutMat, dest);
            return dest;
        }
    }
}
