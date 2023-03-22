using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MajiroRCT
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var rctFile = new RCTFile();

            var img = rctFile.OpenRCImage(@"E:\GalGames_Work\OnWork\(秋晴汉化组)私が好きなら「好き」って言って！\workspace\rct\c_d_browser_.rc8");
            img.Pack();
            img.Unpack();
            img.GetImage().SaveAsPng(@"E:\GalGames_Work\OnWork\(秋晴汉化组)私が好きなら「好き」って言って！\workspace\rct\test2.png");

            rctFile.RCToPNG(@"E:\GalGames_Work\OnWork\(秋晴汉化组)私が好きなら「好き」って言って！\workspace\rct\c_d_browser.rct");
            rctFile.RCToPNG(@"E:\GalGames_Work\OnWork\(秋晴汉化组)私が好きなら「好き」って言って！\workspace\rct\c_d_browser_.rc8");
            rctFile.RCToPNG(@"E:\GalGames_Work\OnWork\(秋晴汉化组)私が好きなら「好き」って言って！\workspace\rct\c_d_browser.rct");
            
            Console.WriteLine("Hello, World!");
        }


    }


    public delegate void DelegateDoCrypt(ref byte[] data);

    public class Binary
    {
        /// <summary>
        /// Copy potentially overlapping sequence of <paramref name="count"/> bytes in array
        /// <paramref name="data"/> from <paramref name="src"/> to <paramref name="dst"/>.
        /// If destination offset resides within source region then sequence will repeat itself.  Widely used
        /// in various compression techniques.
        /// </summary>
        public static void CopyOverlapped(byte[] data, int src, int dst, int count)
        {
            if (dst > src)
            {
                while (count > 0)
                {
                    int preceding = Math.Min(dst - src, count);
                    Buffer.BlockCopy(data, src, data, dst, preceding);
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                Buffer.BlockCopy(data, src, data, dst, count);
            }
        }
    }

    abstract class RCImage
    {
        public uint m_width;
        public uint m_height;
        public uint m_offsetX;
        public uint m_offsetY;
        public uint m_bpp;
        public byte[]? m_pixelData;
        public byte[]? m_rctRawData;
        public sbyte[]? m_shiftTable;

        public abstract bool OpenFromRCT(ref BinaryReader br, DelegateDoCrypt? crypt);
        public abstract Image<Rgb24> GetImage();
        public void Unpack()
        {
            Debug.Assert(m_rctRawData != null);
            Debug.Assert(m_shiftTable != null);
            Debug.Assert(m_bpp > 0);
            var byteDepth = (int)m_bpp / 8;
            var countMask = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            var shiftMask = 3 - (m_shiftTable.Length >> 5);

            m_pixelData = new byte[m_width * m_height * byteDepth];
            var m_input = new MemoryStream(m_rctRawData);

            int data_pos = 0;
            int eax = 0;
            int pixels_remaining = m_pixelData.Length;
            while (pixels_remaining > 0)
            {
                int count = (eax * byteDepth) + byteDepth;
                if (count > pixels_remaining)
                    throw new Exception("Invalid Pixel Data");
                pixels_remaining -= count;

                if (count != m_input.Read(m_pixelData, data_pos, count))
                    throw new Exception("Invalid Pixel Data");
                data_pos += count;

                while (pixels_remaining > 0)
                {
                    eax = m_input.ReadByte();
                    if ((eax & 0x80) == 0)
                    {
                        if (eax == 0x7F)
                            eax += (ushort)(m_input.ReadByte() | m_input.ReadByte() << 8);
                        break;
                    }
                    int shift_index = eax >> shiftMask;
                    
                    eax &= countMask;
                    if (eax == countMask)
                        eax += (ushort)(m_input.ReadByte() | m_input.ReadByte() << 8);

                    count = (eax * byteDepth) + 3;
                    if (pixels_remaining < count)
                        throw new Exception("Invalid Pixel Data");
                    pixels_remaining -= count;
                    int shift = m_shiftTable[shift_index % m_shiftTable.Length];
                    int shift_row = shift & 0x0f;
                    shift >>= 4;
                    shift_row *= (int)m_width;
                    shift -= shift_row;
                    shift *= byteDepth;
                    if (shift >= 0 || data_pos + shift < 0)
                        throw new Exception("Invalid Pixel Data");
                    Binary.CopyOverlapped(m_pixelData, data_pos + shift, data_pos, count);
                    data_pos += count;
                }
            }
        }


        struct ChunkPosition
        {
            public ushort Offset;
            public ushort Length;
        }

        public void Pack()
        {
            Debug.Assert(m_shiftTable != null);
            Debug.Assert(m_pixelData != null);

            const int MaxThunkSize = 0xffff + 0x7f;
            const int MaxMatchSize = 0xffff;

            var shiftTable = new int[m_shiftTable.Length];

            int factor = (int)m_bpp / 8;
            var factorShift = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            var factorShift2 = 3 - (m_shiftTable.Length >> 5);

            for (int i = 0; i < m_shiftTable.Length; ++i)
            {
                int shift = m_shiftTable[i];
                int shift_row = shift & 0x0f;
                shift >>= 4;
                shift_row *= (int)m_width;
                shift -= shift_row;
                shiftTable[i] = shift;
            }

            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            List<byte> m_buffer = new();
            int m_buffer_size = 0;


            var current = 0;
            for (int i = 0; i < factor; i++)
                bw.Write(m_pixelData[current++]);

            m_buffer.Clear();

            int last = m_pixelData.Length / factor;
            current /= factor;

            while (current != last)
            {
                var buf_end = Math.Min(current + MaxMatchSize, last);
                ChunkPosition chunk_pos = new ChunkPosition { Offset = 0, Length = 0 };
                for (int i = 0; i < m_shiftTable.Length; ++i)
                {
                    int offset = current + shiftTable[i];
                    if (offset < 0)
                        continue;
                    if (!ComparePixel(m_pixelData, offset, current, factor))
                        continue;
                    var first1 = current + 1;
                    int first2 = offset + 1;
                    while (first1 != buf_end && ComparePixel(m_pixelData, first1, first2, factor))
                    {
                        ++first1;
                        ++first2;
                    }
                    int weight = first2 - offset;
                    if (weight > chunk_pos.Length)
                    {
                        chunk_pos.Offset = (ushort)i;
                        chunk_pos.Length = (ushort)weight;
                    }
                }

                if (chunk_pos.Length > 0)
                {
                    if (0 != m_buffer.Count)
                    {
                        if (m_buffer_size > 0x7f)
                        {
                            bw.Write((byte)0x7f);
                            bw.Write((ushort)(m_buffer_size - 0x80));
                        }
                        else
                            bw.Write((byte)(m_buffer_size - 1));
                        foreach (var b in m_buffer)
                            bw.Write(b);
                        m_buffer.Clear();
                        m_buffer_size = 0;
                    }
                    int code = (chunk_pos.Offset << factorShift2) | 0x80;
                    if (chunk_pos.Length > factorShift)
                        code |= factorShift;
                    else
                        code |= chunk_pos.Length - 1;

                    bw.Write((byte)code);
                    if (chunk_pos.Length > factorShift)
                        bw.Write((ushort)(chunk_pos.Length - 9));
                    current += chunk_pos.Length;
                }
                else
                {
                    if (m_buffer.Count > 0 && MaxThunkSize == m_buffer_size)
                    {
                        if (m_buffer_size > 0x7f)
                        {
                            bw.Write((byte)0x7f);
                            bw.Write((ushort)(m_buffer_size - 0x80));
                        }
                        else
                            bw.Write((byte)(m_buffer_size - 1));
                        foreach (var b in m_buffer)
                            bw.Write(b);
                        m_buffer.Clear();
                        m_buffer_size = 0;
                    }
                    for(int i = 0; i < factor; i++)
                        m_buffer.Add(m_pixelData[current * factor + i]);
                    current++;
                    ++m_buffer_size;
                }
            }
            if (0 != m_buffer.Count)
            {
                if (m_buffer_size > 0x7f)
                {
                    bw.Write((byte)0x7f);
                    bw.Write((ushort)(m_buffer_size - 0x80));
                }
                else
                    bw.Write((byte)(m_buffer_size - 1));
                foreach (var b in m_buffer)
                    bw.Write(b);
                m_buffer.Clear();
            }

            m_rctRawData = ms.ToArray();
        }

        static bool ComparePixel(byte[] pixelData, int indexA, int indexB, int factor)
        {
            indexA *= factor;
            indexB *= factor;
            for(int i = 0; i < factor; i++)
            {
                if (pixelData[indexA + i] != pixelData[indexB + i])
                    return false;
            }
            return true;
        }
    }

    class RCTImage : RCImage
    {
        public RCTImage()
        {
            m_bpp = 24;
            m_width = 0;
            m_height = 0;
            m_offsetX = 0;
            m_offsetY = 0;
            m_shiftTable = new sbyte[]
            {
                -16, -32, -48, -64, -80, -96,  49,  33,
                 17,   1, -15, -31, -47,  50,  34,  18,
                  2, -14, -30, -46,  51,  35,  19,   3,
                -13, -29, -45,  36,  20,   4, -12, -28,
            };
        }

        public override bool OpenFromRCT(ref BinaryReader br, DelegateDoCrypt? crypt) 
        {
            var isEnc = br.ReadByte() == 0x53;//TC or TS
            Debug.Assert(br.ReadByte() == 0x30);
            var version = br.ReadByte() - 0x30;
            if (version != 0)//Fixme: version == 1
                throw new Exception("Invalid RCT version!");

            m_width = br.ReadUInt32();
            m_height = br.ReadUInt32();
            var dataSize = br.ReadInt32();
            Debug.Assert(dataSize > 0);
            m_rctRawData = br.ReadBytes(dataSize);

            if(isEnc == true)
            {
                if (crypt == null)
                    throw new Exception("Missing Crypto Delegate");

                crypt(ref m_rctRawData);
            }
            Unpack();
            return true;
        }

        public override Image<Rgb24> GetImage()
        {
            Debug.Assert(m_pixelData != null);

            var img = new Image<Rgb24>((int)m_width, (int)m_height);

            for (int y = 0; y < m_height; y++)
            {
                for (int x = 0; x < m_width; x++)
                {
                    var index = (y * m_width + x) * 3;
                    img[x, y] = new Rgb24(m_pixelData[index + 2], m_pixelData[index + 1], m_pixelData[index]);
                }
            }

            return img;
        }
    }

    class RC8Image : RCImage
    {
        public Rgb24[]? m_palette;

        public RC8Image()
        {
            m_bpp = 8;
            m_width = 0;
            m_height = 0;
            m_offsetX = 0;
            m_offsetY = 0;
            m_shiftTable = new sbyte[]
            {
                -16, -32, -48, -64,  49,  33,  17,   1,
                -15, -31, -47,  34,  18,   2, -14, -30,
            };
        }

        public override bool OpenFromRCT(ref BinaryReader br, DelegateDoCrypt? crypt)
        {
            br.BaseStream.Position += 3;//Seems no encryption

            m_width = br.ReadUInt32();
            m_height = br.ReadUInt32();
            var dataSize = br.ReadInt32();
            var paletteData = br.ReadBytes(0x300);
            Debug.Assert(paletteData.Length == 0x300);

            m_palette = new Rgb24[256];
            for(int i = 0; i < 256; i++)
                m_palette[i] = new Rgb24(paletteData[i*3 + 1], paletteData[i*3 + 1], paletteData[i*3]);

            Debug.Assert(dataSize > 0);
            m_rctRawData = br.ReadBytes(dataSize);
            Unpack();

            return true;
        }

        public override Image<Rgb24> GetImage()
        {
            Debug.Assert(m_palette != null);
            Debug.Assert(m_pixelData != null);

            var img = new Image<Rgb24>((int)m_width, (int)m_height);

            for(int y = 0; y < m_height; y++)
            {
                for(int x = 0; x < m_width; x++)
                {
                    img[x, y] = m_palette[m_pixelData[y * m_width + x]];
                }
            }

            return img;
        }
    }

    class RCTFile
    {
        readonly byte[] m_imgKey;

        public RCTFile(string password = "chuable") 
        {
            byte[] bin_pass = Encoding.GetEncoding("shift_jis").GetBytes(password);
            uint crc32 = Crc32.Compute(bin_pass, 0, bin_pass.Length);
            m_imgKey = new byte[0x400];

            for (uint i = 0; i < 0x100; ++i)
                EndianHelper.WriteUint32LE(ref m_imgKey, i*4, crc32 ^ Crc32.Table[(i + crc32) & 0xFF]);
        }
        
        void DoCrypt(ref byte[] data)
        {
            /*int offset = 0x1C + 8 * data.ToInt32(0x18);
            if (offset < 0x1C || offset >= data.Length - 4)
                return;
            int count = data.ToInt32(offset);
            offset += 4;
            if (count <= 0 || count > data.Length - offset)
                return;
            Debug.Assert(Crc32.Table.Length == 0x100);
            unsafe
            {
                fixed (uint* table = Crc32.Table)
                {
                    byte* key = (byte*)table;
                    for (int i = 0; i < count; ++i)
                        data[offset + i] ^= key[i & 0x3FF];
                }
            }*/
        }

        public RCImage OpenRCImage(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var br = new BinaryReader(fs);

            Debug.Assert(br.ReadUInt32() == 0x9A925A98);//六丁

            var type = br.ReadByte();
            if (type == 0x38)//'8' -> RC8
            {
                var ret = new RC8Image();
                ret.OpenFromRCT(ref br, null);

                return ret;
            }
            else if(type == 0x54) // 'T' -> RCT
            {
                var ret = new RCTImage();
                ret.OpenFromRCT(ref br, new DelegateDoCrypt(DoCrypt));

                return ret;
            }
            else
            {
                throw new Exception("Unknown Image Format!");
            }
        }

        public void RCToPNG(string path)
        {
            var rc8ImagePath = path.Replace(".rct", "_.rc8");
            if(path != rc8ImagePath && File.Exists(rc8ImagePath))
            {
                BlendRCTImages(OpenRCImage(path) as RCTImage, OpenRCImage(rc8ImagePath) as RC8Image).SaveAsPng(Path.ChangeExtension(path, "png"));
            }
            else
            {
                OpenRCImage(path).GetImage().SaveAsPng(Path.ChangeExtension(path, "png"));
            }
        }

        Image<Bgra32> BlendRCTImages(RCTImage? rgbImage, RC8Image? alphaImage)
        {
            Debug.Assert(rgbImage != null && alphaImage != null);
            Debug.Assert(rgbImage.m_height == alphaImage.m_height && rgbImage.m_width == alphaImage.m_width);
            Debug.Assert(alphaImage.m_palette != null);
            Debug.Assert(alphaImage.m_pixelData != null);
            Debug.Assert(rgbImage.m_pixelData != null);

            //var img = new Image<Rgba32>((int)rgbImage.m_width, (int)rgbImage.m_height);
            var pixelData = new byte[rgbImage.m_width * rgbImage.m_height * 4];

            for (int y = 0; y < rgbImage.m_height; y++)
            {
                for (int x = 0; x < rgbImage.m_width; x++)
                {
                    var index = y * rgbImage.m_width + x;
                    var alphaPix = alphaImage.m_palette[alphaImage.m_pixelData[index]];

                    pixelData[index * 4 + 0] = rgbImage.m_pixelData[index * 3 + 0];
                    pixelData[index * 4 + 1] = rgbImage.m_pixelData[index * 3 + 1];
                    pixelData[index * 4 + 2] = rgbImage.m_pixelData[index * 3 + 2];
                    pixelData[index * 4 + 3] = (byte)(255 - (alphaPix.R + alphaPix.G + alphaPix.B) / 3);
                }
            }
            return Image.LoadPixelData<Bgra32>(pixelData, (int)rgbImage.m_width, (int)rgbImage.m_height);
        }
    }

    public sealed class Crc32
    {
        private static readonly uint[] crc_table = InitializeTable();

        private uint m_crc = uint.MaxValue;

        public static uint[] Table => crc_table;

        public uint Value => m_crc ^ 0xFFFFFFFFu;

        private static uint[] InitializeTable()
        {
            uint[] array = new uint[256];
            for (uint num = 0u; num < 256; num++)
            {
                uint num2 = num;
                for (int i = 0; i < 8; i++)
                {
                    num2 = (((num2 & 1) == 0) ? (num2 >> 1) : (0xEDB88320u ^ (num2 >> 1)));
                }

                array[num] = num2;
            }

            return array;
        }

        public static uint UpdateCrc(uint crc, byte[] buf, int pos, int len)
        {
            uint num = crc;
            for (int i = 0; i < len; i++)
            {
                num = crc_table[(num ^ buf[pos + i]) & 0xFF] ^ (num >> 8);
            }

            return num;
        }

        public static uint Compute(byte[] buf, int pos, int len)
        {
            return UpdateCrc(uint.MaxValue, buf, pos, len) ^ 0xFFFFFFFFu;
        }

        public void Update(byte[] buf, int pos, int len)
        {
            m_crc = UpdateCrc(m_crc, buf, pos, len);
        }
    }

    public class EndianHelper
    {
        public static void WriteUint32LE(ref byte[] dst, uint pos, uint val)
        {
            dst[pos + 0] = (byte)(val >> 24);
            dst[pos + 1] = (byte)(val >> 16);
            dst[pos + 2] = (byte)(val >> 8);
            dst[pos + 3] = (byte)(val);
        }
    }
}