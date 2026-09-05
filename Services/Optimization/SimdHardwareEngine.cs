using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace WPFImageTransfer.Services.Optimization
{
    /// <summary>
    /// Module tối ưu hóa: Bộ xử lý điểm ảnh đa luồng SIMD phần cứng (Pure C# Hardware Intrinsics)
    /// Tận dụng tập lệnh AVX2/SSE của CPU để xử lý 32 byte dữ liệu điểm ảnh cùng lúc trong 1 nhịp đồng hồ.
    /// Code chay 100% không dùng thư viện ngoài, có cơ chế Fallback tự động cho mọi dòng CPU.
    /// </summary>
    public static class SimdHardwareEngine
    {
        public static readonly bool IsAvx2Supported = Avx2.IsSupported;

        /// <summary>
        /// Đảo ngược màu (Invert) song song SIMD 32 byte/nhịp.
        /// </summary>
        public static unsafe void InvertColorsSimd(byte[] pixels)
        {
            fixed (byte* p = pixels)
            {
                IntPtr basePtr = (IntPtr)p;
                int length = pixels.Length;
                int vectorSize = Vector256<byte>.Count; // 32 bytes
                int simdLength = length - (length % vectorSize);

                if (IsAvx2Supported)
                {
                    var v255 = Vector256.Create((byte)255);
                    Parallel.For(0, simdLength / vectorSize, i =>
                    {
                        byte* ptr = (byte*)basePtr.ToPointer() + (i * vectorSize);
                        var vData = Avx2.LoadVector256(ptr);
                        var vResult = Avx2.Subtract(v255, vData);
                        Avx2.Store(ptr, vResult);
                    });
                }

                // Xử lý nốt các byte lẻ còn lại
                byte* remainPtr = (byte*)basePtr.ToPointer();
                for (int i = simdLength; i < length; i++)
                {
                    remainPtr[i] = (byte)(255 - remainPtr[i]);
                }
            }
        }

        /// <summary>
        /// Điều chỉnh độ sáng đa luồng phần cứng có bão hòa (Saturate không bị tràn quá 255 hay dưới 0).
        /// </summary>
        public static unsafe void AdjustBrightnessSimd(byte[] pixels, int brightnessDelta)
        {
            if (brightnessDelta == 0) return;

            fixed (byte* p = pixels)
            {
                IntPtr basePtr = (IntPtr)p;
                int length = pixels.Length;
                int vectorSize = Vector256<byte>.Count; // 32 bytes
                int simdLength = length - (length % vectorSize);

                if (IsAvx2Supported && brightnessDelta > 0)
                {
                    byte delta = (byte)Math.Min(255, brightnessDelta);
                    var vDelta = Vector256.Create(delta);

                    Parallel.For(0, simdLength / vectorSize, i =>
                    {
                        byte* ptr = (byte*)basePtr.ToPointer() + (i * vectorSize);
                        var vData = Avx2.LoadVector256(ptr);
                        var vResult = Avx2.AddSaturate(vData, vDelta); // Cộng bão hòa tối đa 255
                        Avx2.Store(ptr, vResult);
                    });
                }
                else if (IsAvx2Supported && brightnessDelta < 0)
                {
                    byte delta = (byte)Math.Min(255, -brightnessDelta);
                    var vDelta = Vector256.Create(delta);

                    Parallel.For(0, simdLength / vectorSize, i =>
                    {
                        byte* ptr = (byte*)basePtr.ToPointer() + (i * vectorSize);
                        var vData = Avx2.LoadVector256(ptr);
                        var vResult = Avx2.SubtractSaturate(vData, vDelta); // Trừ bão hòa tối thiểu 0
                        Avx2.Store(ptr, vResult);
                    });
                }
                else
                {
                    // Fallback chạy đa luồng CPU
                    Parallel.For(0, length, i =>
                    {
                        byte* ptr = (byte*)basePtr.ToPointer();
                        int val = ptr[i] + brightnessDelta;
                        ptr[i] = (byte)Math.Clamp(val, 0, 255);
                    });
                    return;
                }

                // Xử lý các byte lẻ
                byte* tailPtr = (byte*)basePtr.ToPointer();
                for (int i = simdLength; i < length; i++)
                {
                    int val = tailPtr[i] + brightnessDelta;
                    tailPtr[i] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        }

        /// <summary>
        /// Phân ngưỡng nhị phân (Binary Threshold) siêu tốc qua SIMD.
        /// </summary>
        public static unsafe void ThresholdSimd(byte[] pixels, byte threshold)
        {
            fixed (byte* p = pixels)
            {
                IntPtr basePtr = (IntPtr)p;
                int length = pixels.Length;
                int vectorSize = Vector256<byte>.Count; // 32 bytes
                int simdLength = length - (length % vectorSize);

                if (IsAvx2Supported)
                {
                    var vThresh = Vector256.Create(threshold);
                    var v255 = Vector256.Create((byte)255);

                    Parallel.For(0, simdLength / vectorSize, i =>
                    {
                        byte* ptr = (byte*)basePtr.ToPointer() + (i * vectorSize);
                        var vData = Avx2.LoadVector256(ptr);
                        var vMask = Avx2.CompareEqual(Avx2.Max(vData, vThresh), vData);
                        var vResult = Avx2.And(vMask, v255);
                        Avx2.Store(ptr, vResult);
                    });
                }

                byte* tailPtr = (byte*)basePtr.ToPointer();
                for (int i = simdLength; i < length; i++)
                {
                    tailPtr[i] = tailPtr[i] >= threshold ? (byte)255 : (byte)0;
                }
            }
        }
    }
}
