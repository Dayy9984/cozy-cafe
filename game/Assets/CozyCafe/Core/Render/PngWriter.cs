using System;
using System.IO;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Minimal PNG encoder (RGBA8, filter 0 per row, zlib "stored" deflate
    /// blocks). Dependency-free so it compiles on netstandard2.1 and in the
    /// editor host. Output is a fully valid PNG decodable by any reader.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Encode(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("bad size");
            if (rgba == null || rgba.Length != width * height * 4)
                throw new ArgumentException("rgba buffer size mismatch");

            byte[] filtered = new byte[(width * 4 + 1) * height];
            int di = 0;
            for (int y = 0; y < height; y++)
            {
                filtered[di++] = 0; // filter: none
                Buffer.BlockCopy(rgba, y * width * 4, filtered, di, width * 4);
                di += width * 4;
            }

            using (var ms = new MemoryStream())
            {
                ms.Write(Signature, 0, Signature.Length);
                byte[] ihdr = new byte[13];
                WriteBe32(ihdr, 0, (uint)width);
                WriteBe32(ihdr, 4, (uint)height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 6;  // color type RGBA
                WriteChunk(ms, "IHDR", ihdr);
                WriteChunk(ms, "IDAT", ZlibStore(filtered));
                WriteChunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        /// zlib wrapper around uncompressed DEFLATE "stored" blocks.
        private static byte[] ZlibStore(byte[] data)
        {
            int blocks = Math.Max(1, (data.Length + 65534) / 65535);
            byte[] buf = new byte[2 + data.Length + blocks * 5 + 4];
            int o = 0;
            buf[o++] = 0x78; // zlib CMF: deflate, 32k window
            buf[o++] = 0x01; // zlib FLG: valid check bits for 0x78
            int pos = 0;
            do
            {
                int n = Math.Min(65535, data.Length - pos);
                bool last = pos + n >= data.Length;
                buf[o++] = (byte)(last ? 1 : 0);
                buf[o++] = (byte)(n & 0xFF);
                buf[o++] = (byte)((n >> 8) & 0xFF);
                int nn = (~n) & 0xFFFF;
                buf[o++] = (byte)(nn & 0xFF);
                buf[o++] = (byte)((nn >> 8) & 0xFF);
                if (n > 0) Buffer.BlockCopy(data, pos, buf, o, n);
                o += n;
                pos += n;
            } while (pos < data.Length);
            WriteBe32(buf, o, Adler32(data));
            return buf;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBe32(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            byte[] t = { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            s.Write(t, 0, 4);
            if (data.Length > 0) s.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFF;
            foreach (byte b in t) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            byte[] crcBytes = new byte[4];
            WriteBe32(crcBytes, 0, crc ^ 0xFFFFFFFF);
            s.Write(crcBytes, 0, 4);
        }

        private static void WriteBe32(byte[] buf, int offset, uint v)
        {
            buf[offset] = (byte)((v >> 24) & 0xFF);
            buf[offset + 1] = (byte)((v >> 16) & 0xFF);
            buf[offset + 2] = (byte)((v >> 8) & 0xFF);
            buf[offset + 3] = (byte)(v & 0xFF);
        }

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }
    }
}
