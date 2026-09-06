using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;

namespace WPFImageTransfer.Services.Processing
{
    /// <summary>
    /// Thuật toán NIQE (Naturalness Image Quality Evaluator) thuần C# / Emgu.CV
    /// Cài đặt 100% bằng C# và OpenCV C++, không phụ thuộc vào Python hay bất kỳ script ngoài nào.
    /// Dựa trên bài báo gốc của Mittal et al. (IEEE SPL 2013) và bộ tham số mô hình tự nhiên chuẩn.
    /// </summary>
    public static class NiqeCalculator
    {
        private const int PatchSize = 96;
        private static readonly double[] GammaRange;
        private static readonly double[] PrecGammas;

        static NiqeCalculator()
        {
            // Bảng tra cứu alpha và tỷ lệ moment AGGD từ 0.2 đến 10 với bước 0.001 (9800 điểm)
            int steps = 9800;
            GammaRange = new double[steps];
            PrecGammas = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                double alpha = 0.2 + i * 0.001;
                GammaRange[i] = alpha;
                double g1 = GammaLanczos(1.0 / alpha);
                double g2 = GammaLanczos(2.0 / alpha);
                double g3 = GammaLanczos(3.0 / alpha);
                PrecGammas[i] = (g2 * g2) / (g1 * g3);
            }
        }

        public static double? Compute(Mat source)
        {
            try
            {
                using Mat gray = new Mat();
                if (source.NumberOfChannels == 1) source.CopyTo(gray);
                else CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);

                int h = gray.Height;
                int w = gray.Width;

                // Nếu ảnh nhỏ hơn kích thước tối thiểu cho lưới 96x96
                using Mat prepared = new Mat();
                if (h < PatchSize * 2 + 1 || w < PatchSize * 2 + 1)
                {
                    double scale = Math.Max((PatchSize * 2 + 2.0) / h, (PatchSize * 2 + 2.0) / w);
                    CvInvoke.Resize(gray, prepared, new System.Drawing.Size((int)Math.Round(w * scale), (int)Math.Round(h * scale)), 0, 0, Inter.Cubic);
                }
                else
                {
                    gray.CopyTo(prepared);
                }

                h = prepared.Height;
                w = prepared.Width;

                int hoffset = h % PatchSize;
                int woffset = w % PatchSize;
                using Mat cropped = new Mat(prepared, new System.Drawing.Rectangle(0, 0, w - woffset, h - hoffset));

                // Scale 1: Ảnh kích thước gốc (patch size 96)
                using Mat imgFloat1 = new Mat();
                cropped.ConvertTo(imgFloat1, DepthType.Cv32F);
                float[,] mscn1 = ComputeMscn(imgFloat1);

                // Scale 2: Ảnh thu nhỏ 0.5 bằng nội suy Bicubic (patch size 48)
                using Mat croppedHalf = new Mat();
                CvInvoke.Resize(cropped, croppedHalf, new System.Drawing.Size(cropped.Width / 2, cropped.Height / 2), 0, 0, Inter.Cubic);
                using Mat imgFloat2 = new Mat();
                croppedHalf.ConvertTo(imgFloat2, DepthType.Cv32F);
                float[,] mscn2 = ComputeMscn(imgFloat2);

                // Trích xuất đặc trưng trên từng patch
                List<double[]> feats1 = ExtractOnPatches(mscn1, cropped.Height, cropped.Width, PatchSize);
                List<double[]> feats2 = ExtractOnPatches(mscn2, croppedHalf.Height, croppedHalf.Width, PatchSize / 2);

                if (feats1.Count == 0 || feats1.Count != feats2.Count)
                    return null;

                int patchCount = feats1.Count;
                double[][] allFeats = new double[patchCount][];
                for (int i = 0; i < patchCount; i++)
                {
                    double[] combined = new double[36];
                    Array.Copy(feats1[i], 0, combined, 0, 18);
                    Array.Copy(feats2[i], 0, combined, 18, 18);
                    allFeats[i] = combined;
                }

                // Tính kỳ vọng mẫu (sample_mu: 36 chiều)
                double[] sampleMu = new double[36];
                for (int p = 0; p < patchCount; p++)
                {
                    for (int k = 0; k < 36; k++)
                    {
                        sampleMu[k] += allFeats[p][k];
                    }
                }
                for (int k = 0; k < 36; k++) sampleMu[k] /= patchCount;

                // Tính hiệp phương sai mẫu (sample_cov: 36x36)
                double[,] sampleCov = new double[36, 36];
                if (patchCount > 1)
                {
                    for (int p = 0; p < patchCount; p++)
                    {
                        for (int j = 0; j < 36; j++)
                        {
                            double dj = allFeats[p][j] - sampleMu[j];
                            for (int k = 0; k < 36; k++)
                            {
                                double dk = allFeats[p][k] - sampleMu[k];
                                sampleCov[j, k] += dj * dk;
                            }
                        }
                    }
                    double denom = patchCount - 1;
                    for (int j = 0; j < 36; j++)
                        for (int k = 0; k < 36; k++)
                            sampleCov[j, k] /= denom;
                }

                // Tính khoảng cách Mahalanobis: X = sampleMu - popMu
                double[] x = new double[36];
                for (int k = 0; k < 36; k++)
                {
                    x[k] = sampleMu[k] - NiqeModelParameters.PopMu[k];
                }

                // CovMat = (PopCov + sampleCov) / 2
                using Mat covMat = new Mat(36, 36, DepthType.Cv64F, 1);
                unsafe
                {
                    double* ptr = (double*)covMat.DataPointer;
                    int stepD = covMat.Step / sizeof(double);
                    for (int r = 0; r < 36; r++)
                    {
                        for (int c = 0; c < 36; c++)
                        {
                            ptr[r * stepD + c] = (NiqeModelParameters.PopCov[r, c] + sampleCov[r, c]) / 2.0;
                        }
                    }
                }

                // Nghịch đảo giả (Pseudo-inverse qua SVD)
                using Mat pinvMat = new Mat(36, 36, DepthType.Cv64F, 1);
                CvInvoke.Invert(covMat, pinvMat, DecompMethod.Svd);

                // Tính x^T * pinv * x
                double distanceSq = 0;
                unsafe
                {
                    double* pinvPtr = (double*)pinvMat.DataPointer;
                    int stepP = pinvMat.Step / sizeof(double);
                    for (int r = 0; r < 36; r++)
                    {
                        double rowSum = 0;
                        for (int c = 0; c < 36; c++)
                        {
                            rowSum += pinvPtr[r * stepP + c] * x[c];
                        }
                        distanceSq += x[r] * rowSum;
                    }
                }

                if (distanceSq < 0 || double.IsNaN(distanceSq)) return null;
                return Math.Sqrt(distanceSq);
            }
            catch
            {
                return null;
            }
        }

        private static float[,] ComputeMscn(Mat imgFloat)
        {
            int h = imgFloat.Height;
            int w = imgFloat.Width;

            using Mat mu = new Mat();
            using Mat imgSq = new Mat();
            using Mat muSq = new Mat();
            using Mat muMu = new Mat();
            using Mat varMat = new Mat();
            using Mat sigma = new Mat();
            using Mat sigmaPlusC = new Mat();
            using Mat diff = new Mat();
            using Mat mscnMat = new Mat();

            CvInvoke.GaussianBlur(imgFloat, mu, new System.Drawing.Size(7, 7), 7.0 / 6.0, 7.0 / 6.0, BorderType.Constant);
            CvInvoke.Multiply(imgFloat, imgFloat, imgSq);
            CvInvoke.GaussianBlur(imgSq, muSq, new System.Drawing.Size(7, 7), 7.0 / 6.0, 7.0 / 6.0, BorderType.Constant);

            CvInvoke.Multiply(mu, mu, muMu);
            CvInvoke.AbsDiff(muSq, muMu, varMat);
            CvInvoke.Sqrt(varMat, sigma);

            using ScalarArray cArr = new ScalarArray(1.0);
            CvInvoke.Add(sigma, cArr, sigmaPlusC);
            CvInvoke.Subtract(imgFloat, mu, diff);
            CvInvoke.Divide(diff, sigmaPlusC, mscnMat);

            float[,] result = new float[h, w];
            unsafe
            {
                float* pData = (float*)mscnMat.DataPointer;
                int stepF = mscnMat.Step / sizeof(float);
                for (int r = 0; r < h; r++)
                {
                    float* row = pData + r * stepF;
                    for (int c = 0; c < w; c++)
                    {
                        result[r, c] = row[c];
                    }
                }
            }
            return result;
        }

        private static List<double[]> ExtractOnPatches(float[,] mscn, int h, int w, int patchSize)
        {
            List<double[]> list = new List<double[]>();
            for (int j = 0; j <= h - patchSize; j += patchSize)
            {
                for (int i = 0; i <= w - patchSize; i += patchSize)
                {
                    float[] patch = new float[patchSize * patchSize];
                    int idx = 0;
                    for (int r = 0; r < patchSize; r++)
                    {
                        for (int c = 0; c < patchSize; c++)
                        {
                            patch[idx++] = mscn[j + r, i + c];
                        }
                    }
                    list.Add(ExtractSubbandFeats(patch, patchSize));
                }
            }
            return list;
        }

        private static double[] ExtractSubbandFeats(float[] patch, int patchSize)
        {
            var (alphaM, nM, blM, brM) = FitAggd(patch);

            PairedProducts(patch, patchSize, out float[] h, out float[] v, out float[] d1, out float[] d2);
            var (a1, n1, bl1, br1) = FitAggd(h);
            var (a2, n2, bl2, br2) = FitAggd(v);
            var (a3, n3, bl3, br3) = FitAggd(d1);
            var (a4, n4, bl4, br4) = FitAggd(d2);

            return new double[18]
            {
                alphaM, (blM + brM) / 2.0,
                a1, n1, bl1, br1,
                a2, n2, bl2, br2,
                a3, n3, bl3, bl3, // D1 dùng bl3, bl3 theo quy ước bài báo
                a4, n4, bl4, bl4  // D2 dùng bl4, bl4 theo quy ước bài báo
            };
        }

        private static void PairedProducts(float[] p, int size, out float[] h, out float[] v, out float[] d1, out float[] d2)
        {
            int count = size * size;
            h = new float[count];
            v = new float[count];
            d1 = new float[count];
            d2 = new float[count];

            for (int r = 0; r < size; r++)
            {
                int rUp = (r - 1 + size) % size; // roll axis 0 by +1
                for (int c = 0; c < size; c++)
                {
                    int cLeft = (c - 1 + size) % size;  // roll axis 1 by +1
                    int cRight = (c + 1) % size;       // roll axis 1 by -1
                    int curIdx = r * size + c;

                    float val = p[curIdx];
                    h[curIdx] = p[r * size + cLeft] * val;
                    v[curIdx] = p[rUp * size + c] * val;
                    d1[curIdx] = p[rUp * size + cLeft] * val;
                    d2[curIdx] = p[rUp * size + cRight] * val;
                }
            }
        }

        private static (double alpha, double n, double bl, double br) FitAggd(float[] data)
        {
            int len = data.Length;
            double leftSumSq = 0;
            int leftCount = 0;
            double rightSumSq = 0;
            int rightCount = 0;
            double absSum = 0;
            double totalSumSq = 0;

            for (int i = 0; i < len; i++)
            {
                float v = data[i];
                double sq = v * v;
                totalSumSq += sq;
                absSum += Math.Abs(v);
                if (v < 0)
                {
                    leftSumSq += sq;
                    leftCount++;
                }
                else
                {
                    rightSumSq += sq;
                    rightCount++;
                }
            }

            double leftMeanSqrt = leftCount > 0 ? Math.Sqrt(leftSumSq / leftCount) : 0;
            double rightMeanSqrt = rightCount > 0 ? Math.Sqrt(rightSumSq / rightCount) : 0;

            double gammaHat = rightMeanSqrt != 0 ? leftMeanSqrt / rightMeanSqrt : double.PositiveInfinity;
            double meanSq = totalSumSq / len;
            double avgAbs = absSum / len;
            double rHat = meanSq != 0 ? (avgAbs * avgAbs) / meanSq : double.PositiveInfinity;

            double gh2 = gammaHat * gammaHat;
            double gh3 = gh2 * gammaHat;
            double denom = Math.Pow(gh2 + 1.0, 2.0);
            double rhatNorm = rHat * (((gh3 + 1.0) * (gammaHat + 1.0)) / (denom != 0 ? denom : 1.0));

            // Tìm alpha tối ưu trong bảng PrecGammas bằng Binary Search
            int bestIdx = 0;
            double minDiff = double.MaxValue;
            for (int i = 0; i < PrecGammas.Length; i++)
            {
                double d = Math.Abs(PrecGammas[i] - rhatNorm);
                if (d < minDiff)
                {
                    minDiff = d;
                    bestIdx = i;
                }
                else if (d > minDiff)
                {
                    break; // Do PrecGammas đơn điệu tăng nên có thể dừng sớm
                }
            }

            double alpha = GammaRange[bestIdx];
            double gam1 = GammaLanczos(1.0 / alpha);
            double gam2 = GammaLanczos(2.0 / alpha);
            double gam3 = GammaLanczos(3.0 / alpha);

            double aggdRatio = Math.Sqrt(gam1) / Math.Sqrt(gam3);
            double bl = aggdRatio * leftMeanSqrt;
            double br = aggdRatio * rightMeanSqrt;
            double n = (br - bl) * (gam2 / gam1);

            return (alpha, n, bl, br);
        }

        private static double GammaLanczos(double z)
        {
            if (z < 0.5) return Math.PI / (Math.Sin(Math.PI * z) * GammaLanczos(1.0 - z));
            z -= 1.0;
            double x = 0.99999999999980993;
            double[] p = {
                676.5203681218851, -1259.1392167224028, 771.32342877765313,
                -176.61502916214059, 12.507343278686905, -0.138571095836524,
                9.9843695780195716e-6, 1.5056327351493116e-7
            };
            for (int i = 0; i < p.Length; i++)
                x += p[i] / (z + i + 1.0);
            double t = z + 7.5;
            return Math.Sqrt(2.0 * Math.PI) * Math.Pow(t, z + 0.5) * Math.Exp(-t) * x;
        }
    }
}
