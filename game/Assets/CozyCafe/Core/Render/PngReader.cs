using System;
using System.IO;
using System.IO.Compression;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Minimal PNG decoder producing RGBA8 pixel buffers - the inverse of
    /// PngWriter, so art approved by the pipeline is loaded through the same
    /// canonical format the renderer emits. Handles color types 6 (RGBA) and
    /// 2 (RGB), bit depth 8, non-interlaced, all five scanline filters, and
    /// both stored-block and compressed DEFLATE IDAT payloads.
    /// </summary>
    public static class PngReader
    {
        private static readonly byte[] Signature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// Decodes a PNG file into a SoftwareCanvas. Returns false on any
        /// structural problem - callers treat failure as missing art, never
        /// as an assumed image.
        public static bool TryLoad(string path, out SoftwareCanvas canvas)
        {
            canvas = null;
            byte[] file;
            try { file = File.ReadAllBytes(path); }
            catch (Exception) { return false; }
            return TryDecode(file, out canvas);
        }

        public static bool TryDecode(byte[] file, out SoftwareCanvas canvas)
        {
            canvas = null;
            if (file == null || file.Length < 33) return false;
            for (int i = 0; i < 8; i++) if (file[i] != Signature[i]) return false;

            int width = 0, height = 0, colorType = -1, bitDepth = 0;
            bool interlaced = true;
            byte[] idat = null;
            int pos = 8;
            while (pos + 12 <= file.Length)
            {
                int len = ReadBe32(file, pos);
                if (len < 0 || pos + 12 + len > file.Length) return false;
                string type = Encoding(file, pos + 4, 4);
                if (type == "IHDR")
                {
                    width = ReadBe32(file, pos + 8);
                    height = ReadBe32(file, pos + 12);
                    bitDepth = file[pos + 16];
                    colorType = file[pos + 17];
                    interlaced = file[pos + 20] != 0;
                }
                else if (type == "IDAT")
                {
                    var seg = new byte[len];
                    Buffer.BlockCopy(file, pos + 8, seg, 0, len);
                    idat = idat == null ? seg : Concat(idat, seg);
                }
                pos += 12 + len;
            }
            if (width <= 0 || height <= 0 || interlaced || idat == null
                || bitDepth != 8 || (colorType != 6 && colorType != 2))
            {
                return false;
            }

            byte[] raw = Inflate(idat);
            if (raw == null) return false;

            int bpp = colorType == 6 ? 4 : 3;
            int stride = width * bpp;
            if (raw.Length < (stride + 1) * height) return false;

            byte[] rgba = new byte[width * height * 4];
            byte[] prev = new byte[stride];
            byte[] cur = new byte[stride];
            int rp = 0;
            for (int y = 0; y < height; y++)
            {
                int filter = raw[rp++];
                Buffer.BlockCopy(raw, rp, cur, 0, stride);
                rp += stride;
                Unfilter(cur, prev, stride, bpp, filter);
                for (int x = 0; x < width; x++)
                {
                    int si = x * bpp, di = (y * width + x) * 4;
                    rgba[di] = cur[si];
                    rgba[di + 1] = cur[si + 1];
                    rgba[di + 2] = cur[si + 2];
                    rgba[di + 3] = colorType == 6 ? cur[si + 3] : (byte)255;
                }
                var swap = prev; prev = cur; cur = swap;
            }

            var cv = new SoftwareCanvas(width, height);
            Buffer.BlockCopy(rgba, 0, cv.Pixels, 0, rgba.Length);
            canvas = cv;
            return true;
        }

        /// Inflates a zlib stream: stored-block path first (our own writers),
        /// then a general DEFLATE path via DeflateStream.
        private static byte[] Inflate(byte[] z)
        {
            byte[] stored;
            if (TryInflateStored(z, out stored)) return stored;
            try
            {
                using (var input = new MemoryStream(z))
                using (var ds = new DeflateStream(input, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch (Exception) { }
            try
            {
                // Some DeflateStream builds want the 2-byte zlib header off.
                using (var input = new MemoryStream(z, 2, z.Length - 2))
                using (var ds = new DeflateStream(input, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch (Exception) { return null; }
        }

        private static bool TryInflateStored(byte[] z, out byte[] data)
        {
            data = null;
            if (z.Length < 6 || (z[0] & 0x0F) != 8) return false;
            using (var ms = new MemoryStream())
            {
                int p = 2;
                bool final = false;
                while (!final)
                {
                    if (p >= z.Length - 4) return false;
                    int header = z[p];
                    final = (header & 1) != 0;
                    int btype = (header >> 1) & 3;
                    if (btype != 0) return false; // compressed - other path
                    p++;
                    if (p + 4 > z.Length) return false;
                    int len = z[p] | (z[p + 1] << 8);
                    int nlen = z[p + 2] | (z[p + 3] << 8);
                    if ((len ^ nlen) != 0xFFFF) return false;
                    p += 4;
                    if (p + len > z.Length) return false;
                    ms.Write(z, p, len);
                    p += len;
                }
                data = ms.ToArray();
                return true;
            }
        }

        private static void Unfilter(byte[] cur, byte[] prev, int stride,
            int bpp, int filter)
        {
            switch (filter)
            {
                case 0: return;
                case 1:
                    for (int i = bpp; i < stride; i++)
                        cur[i] = (byte)(cur[i] + cur[i - bpp]);
                    return;
                case 2:
                    for (int i = 0; i < stride; i++)
                        cur[i] = (byte)(cur[i] + prev[i]);
                    return;
                case 3:
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= bpp ? cur[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + ((left + prev[i]) >> 1));
                    }
                    return;
                case 4:
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0;
                        int b = prev[i];
                        int c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        cur[i] = (byte)(cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                    }
                    return;
                default: return;
            }
        }

        private static int ReadBe32(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        private static string Encoding(byte[] b, int o, int n)
        {
            return System.Text.Encoding.ASCII.GetString(b, o, n);
        }
    }
}
