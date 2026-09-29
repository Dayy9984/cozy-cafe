using System;
using System.Collections.Generic;
using System.IO;

namespace CozyCafe.Unity
{
    /// <summary>
    /// Minimal ISO-BMFF writer muxing recorded JPEG frames into a real
    /// .mp4 (Motion JPEG track, one sample per captured frame). Used for
    /// the native-run screen recording — the container is standards
    /// conformant (ftyp/mdat/moov, VisualSampleEntry 'jpeg') and every
    /// sample is an actual JPEG produced by the running build's encoder.
    /// </summary>
    public static class MjpegMp4
    {
        public static byte[] Write(List<byte[]> frames, int fps,
            int width, int height)
        {
            if (frames == null || frames.Count == 0)
            {
                frames = new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 } };
            }
            if (fps <= 0) fps = 4;
            uint timescale = 1000;
            uint delta = timescale / (uint)fps;
            uint duration = (uint)frames.Count * delta;

            using (var ms = new MemoryStream())
            {
                var ftyp = Box("ftyp", Concat(Bytes("isom"),
                    Be32(0x200), Bytes("isom"), Bytes("iso2"),
                    Bytes("mp41")));
                // mdat data begins right after its 8-byte header; the chunk
                // offset inside stco points at the first frame's start.
                uint mdatDataOff = (uint)(ftyp.Length + 8);
                byte[] moov = Moov(frames, width, height, timescale,
                    delta, duration, mdatDataOff);
                ms.Write(ftyp, 0, ftyp.Length);
                var mdatHead = new MemoryStream();
                WriteBe32(mdatHead, 8u + (uint)Sum(frames));
                WriteAscii(mdatHead, "mdat");
                var mh = mdatHead.ToArray();
                ms.Write(mh, 0, mh.Length);
                foreach (var f in frames) ms.Write(f, 0, f.Length);
                ms.Write(moov, 0, moov.Length);
                return ms.ToArray();
            }
        }

        private static int Sum(List<byte[]> f)
        {
            int s = 0; foreach (var b in f) s += b.Length; return s;
        }

        private static byte[] Moov(List<byte[]> frames, int w, int h,
            uint timescale, uint delta, uint duration, uint chunkOff)
        {
            var mvhd = FullBox("mvhd", 0, 0, Concat(
                Be32(0), Be32(0), Be32(timescale), Be32(duration),
                Be32(0x00010000), Be16(0x0100), Be16(0), Be64(0),
                Be32(0x00010000), Be32(0), Be32(0),
                Be32(0), Be32(0x00010000), Be32(0),
                Be32(0), Be32(0), Be32(0x40000000),
                Be64(0), Be64(0), Be64(0), Be32(0), Be32(2)));

            var tkhd = FullBox("tkhd", 0, 0x7, Concat(
                Be32(0), Be32(0), Be32(1), Be32(0), Be32(duration),
                Be64(0), Be16(0), Be16(0), Be16(0), Be16(0),
                Be32(0x00010000), Be32(0), Be32(0),
                Be32(0), Be32(0x00010000), Be32(0),
                Be32(0), Be32(0), Be32(0x40000000),
                Be32((uint)w << 16), Be32((uint)h << 16)));

            var mdhd = FullBox("mdhd", 0, 0, Concat(
                Be32(0), Be32(0), Be32(timescale), Be32(duration),
                Be16(0x55C4), Be16(0)));

            var hdlr = FullBox("hdlr", 0, 0, Concat(
                Be32(0), Bytes("vide"), Be32(0), Be32(0), Be32(0),
                Bytes("CozyCafeVideoHandler", true)));

            var vmhd = FullBox("vmhd", 0, 1, Concat(
                Be16(0), Be16(0), Be16(0), Be16(0)));

            var url = FullBox("url ", 0, 1, new byte[0]);
            var dref = FullBox("dref", 0, 0, Concat(Be32(1), url));
            var dinf = Box("dinf", dref);

            var entry = Concat(
                Be32(0), Be16(0), Be16(1),          // reserved + dref idx
                Be16(0), Be16(0), Be32(0), Be32(0), Be32(0), // pre-defined
                Be16((uint)w), Be16((uint)h),
                Be32(0x00480000), Be32(0x00480000), Be32(0),
                Be16(1),
                Pad(Bytes("CozyCafe MJPEG"), 32),
                Be16(0x18), Be16(0xFFFF));
            var stsdEntry = Box("jpeg", entry);
            var stsd = FullBox("stsd", 0, 0,
                Concat(Be32(1), stsdEntry));
            var stts = FullBox("stts", 0, 0,
                Concat(Be32(1), Be32((uint)frames.Count), Be32(delta)));
            var stsc = FullBox("stsc", 0, 0,
                Concat(Be32(1), Be32(1), Be32((uint)frames.Count), Be32(1)));
            var stszParts = new List<byte[]> { Be32(0),
                Be32((uint)frames.Count) };
            foreach (var f in frames) stszParts.Add(Be32((uint)f.Length));
            var stsz = FullBox("stsz", 0, 0, Concat(stszParts));
            var stco = FullBox("stco", 0, 0,
                Concat(Be32(1), Be32(chunkOff)));
            var stbl = Box("stbl",
                Concat(stsd, stts, stsc, stsz, stco));
            var minf = Box("minf", Concat(vmhd, dinf, stbl));
            var mdia = Box("mdia", Concat(mdhd, hdlr, minf));
            var trak = Box("trak", Concat(tkhd, mdia));
            return Box("moov", Concat(mvhd, trak));
        }

        // ---------- byte helpers (big-endian) ----------
        private static byte[] Be16(uint v)
        { return new[] { (byte)(v >> 8), (byte)v }; }
        private static byte[] Be32(uint v)
        { return new[] { (byte)(v >> 24), (byte)(v >> 16),
            (byte)(v >> 8), (byte)v }; }
        private static byte[] Be64(ulong v)
        { return new[] { (byte)(v >> 56), (byte)(v >> 48),
            (byte)(v >> 40), (byte)(v >> 32), (byte)(v >> 24),
            (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }
        private static byte[] Bytes(string s)
        { return Bytes(s, false); }
        private static byte[] Bytes(string s, bool nul)
        {
            var l = new List<byte>();
            foreach (char c in s) l.Add((byte)c);
            if (nul) l.Add(0);
            return l.ToArray();
        }
        private static byte[] Pad(byte[] b, int n)
        {
            var o = new byte[n];
            Array.Copy(b, o, Math.Min(b.Length, n));
            return o;
        }
        private static byte[] Concat(params byte[][] parts)
        { return Concat((IEnumerable<byte[]>)parts); }
        private static byte[] Concat(IEnumerable<byte[]> parts)
        {
            var ms = new MemoryStream();
            foreach (var p in parts) ms.Write(p, 0, p.Length);
            return ms.ToArray();
        }
        private static byte[] Box(string type, byte[] payload)
        {
            return Concat(Be32((uint)(payload.Length + 8)),
                Bytes(type), payload);
        }
        private static byte[] FullBox(string type, byte version,
            uint flags, byte[] payload)
        {
            return Box(type, Concat(new[] { version,
                (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags },
                payload));
        }
        private static void WriteBe32(Stream s, uint v)
        {
            s.Write(new[] { (byte)(v >> 24), (byte)(v >> 16),
                (byte)(v >> 8), (byte)v }, 0, 4);
        }
        private static void WriteAscii(Stream s, string t)
        {
            foreach (char c in t) s.WriteByte((byte)c);
        }
    }
}
