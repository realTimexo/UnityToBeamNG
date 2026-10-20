using System;
using System.IO;
using System.IO.Compression;

namespace UnityToBeamNG
{
    /// <summary>Minimal 16-bit grayscale PNG writer (Unity cannot do this natively).</summary>
    public static class BeamNGPng16
    {
        static uint[] crcTable;

        static uint Crc(byte[] type, byte[] data, int len)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            foreach (var b in type) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            for (int i = 0; i < len; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static void Chunk(Stream s, string type, byte[] data, int len)
        {
            var t = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            WriteU32(s, (uint)len);
            s.Write(t, 0, 4);
            s.Write(data, 0, len);
            WriteU32(s, Crc(t, data, len));
        }

        static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        /// <summary>heights: size*size values, row 0 = top image row.</summary>
        public static void WriteGray16(string path, ushort[] heights, int size, bool flipRows)
        {
            using (var fs = File.Create(path))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                ihdr[0] = (byte)(size >> 24); ihdr[1] = (byte)(size >> 16); ihdr[2] = (byte)(size >> 8); ihdr[3] = (byte)size;
                ihdr[4] = ihdr[0]; ihdr[5] = ihdr[1]; ihdr[6] = ihdr[2]; ihdr[7] = ihdr[3];
                ihdr[8] = 16; ihdr[9] = 0; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
                Chunk(fs, "IHDR", ihdr, 13);

                var ms = new MemoryStream();
                ms.WriteByte(0x78); ms.WriteByte(0x01);
                uint a = 1, b = 0;
                var row = new byte[1 + size * 2];
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                {
                    for (int y = 0; y < size; y++)
                    {
                        int srcRow = flipRows ? size - 1 - y : y;
                        row[0] = 0;
                        int o = 1, baseIdx = srcRow * size;
                        for (int x = 0; x < size; x++)
                        {
                            ushort v = heights[baseIdx + x];
                            row[o++] = (byte)(v >> 8); row[o++] = (byte)(v & 0xFF);
                        }
                        for (int i = 0; i < row.Length; i++) { a = (a + row[i]) % 65521; b = (b + a) % 65521; }
                        ds.Write(row, 0, row.Length);
                    }
                }
                WriteU32(ms, (b << 16) | a);
                var buf = ms.GetBuffer();
                Chunk(fs, "IDAT", buf, (int)ms.Length);
                Chunk(fs, "IEND", new byte[0], 0);
            }
        }
    }
}
