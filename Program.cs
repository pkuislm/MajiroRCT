using System.Diagnostics;
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

            if(args.Length < 2)
            {
                Console.WriteLine("RCImageTool v0.1.\nUsage: \nExtract: RCImage.exe -e [RCFile]\nPack: RCImage.exe -p [PNGFile]");
                return;
            }

            switch(args[0])
            {
                case "-e":
                    rctFile.RCToPNG(args[1]);
                    break;
                case "-p":
                    rctFile.PNGToRC(args[1]);
                    break;
                default: 
                    Console.WriteLine($"Unknown arg: \"{args[0]}\"");
                    break;
            }
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

        public abstract bool FromRCT(ref BinaryReader br, DelegateDoCrypt? crypt);
        public bool FromPixels(byte[] pixels, uint width, uint height)
        {
            m_width = width;
            m_height = height;
            m_pixelData = pixels;
            Pack();
            return true;
        }

        public abstract Image<Rgb24> GetImage();

        public abstract byte[] GetRCIamge(DelegateDoCrypt? crypt);

        protected void Unpack()
        {
            Trace.Assert(m_rctRawData != null);
            Trace.Assert(m_shiftTable != null);
            if (m_rctRawData == null || m_shiftTable == null)
            {
                throw new Exception();
            }

            var byteDepth = (int)m_bpp / 8;
            var countMask = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            var shiftStart = 3 - (m_shiftTable.Length >> 5);
            var countBase = byteDepth == 1 ? 3 : 1;

            m_pixelData = new byte[m_width * m_height * byteDepth];
            var reader = new MemoryStream(m_rctRawData);

            //Start by reading 1 pixel
            reader.Read(m_pixelData, 0, byteDepth);

            int dstPos = byteDepth;
            int pixelRemaining = m_pixelData.Length - byteDepth;

            int cmd;
            int count;
            while (pixelRemaining > 0)
            {
                cmd = reader.ReadByte();
                if ((cmd & 0x80) == 0)// Read Run
                {
                    if (cmd == 0x7F)
                        cmd += (ushort)(reader.ReadByte() | reader.ReadByte() << 8);

                    count = (cmd + 1) * byteDepth;

                    if (count > pixelRemaining)
                        throw new Exception("Decompress error: Pixel out of bounds");

                    if (count != reader.Read(m_pixelData, dstPos, count))
                        throw new Exception("Decompress error: Insufficient source data");
                }
                else // Copy Run
                {

                    count = cmd & countMask;
                    if (count == countMask)
                        count += (ushort)(reader.ReadByte() | reader.ReadByte() << 8);

                    count = (count + countBase) * byteDepth;
                    if (pixelRemaining < count)
                        throw new Exception("Decompress error: Pixel out of bounds");

                    // This 'copySourceAdvance' is always negative
                    int copySourceAdvance = m_shiftTable[(cmd >> shiftStart) % m_shiftTable.Length];
                    int shiftRow = copySourceAdvance & 0x0f;
                    copySourceAdvance = (copySourceAdvance >> 4) - (shiftRow * (int)m_width);

                    copySourceAdvance *= byteDepth;

                    if (copySourceAdvance >= 0 || dstPos + copySourceAdvance < 0)
                        throw new Exception("Decompress error: Shift param incorrect");

                    Binary.CopyOverlapped(m_pixelData, dstPos + copySourceAdvance, dstPos, count);
                }

                pixelRemaining -= count;
                dstPos += count;
            }
        }


        struct ChunkPosition
        {
            public int shiftIdx;
            public int matchLen;
        }

        readonly List<byte> m_buffer = new();
        int m_bufferSize = 0;

        // Pack pixels in m_pixelData, dst is m_rctRawData
        protected void Pack()
        {
            Trace.Assert(m_shiftTable != null);
            Trace.Assert(m_pixelData != null);
            if (m_pixelData == null || m_shiftTable == null)
            {
                throw new Exception();
            }

            const int maxThunkSize = 0xffff + 0x7F;
            const int maxMatchSize = 0xffff + 1;

            var shiftTable = new int[m_shiftTable.Length];

            int byteDepth = (int)m_bpp / 8;
            var countMask = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            var shiftStart = 3 - (m_shiftTable.Length >> 5);
            var countBase = byteDepth == 1 ? 3 : 1;
            var ignoreThreshold = byteDepth == 1 ? 2 : 0;

            //InitShiftTable
            for (int i = 0; i < m_shiftTable.Length; ++i)
            {
                int shift = m_shiftTable[i];
                int shiftRow = shift & 0x0f;
                shiftTable[i] = (shift >> 4) - (shiftRow * (int)m_width);
            }

            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            m_buffer.Clear();
            m_bufferSize = 0;

            //First write a pixel
            bw.Write(m_pixelData, 0, byteDepth);

            int current = 1;
            int last = m_pixelData.Length / byteDepth;

            while (current != last)
            {
                var pos = new ChunkPosition { shiftIdx = 0, matchLen = 0 };
                {
                    int maxMatchIdx = Math.Min(current + maxMatchSize, last);
                    //Run through all shifts to see which one is the longest one
                    for (int i = 0; i < m_shiftTable.Length; ++i)
                    {
                        int prevIdx = current + shiftTable[i];

                        // There is not enough data before, skip
                        if (prevIdx < 0)
                            continue;

                        // First pixel is not the same, skip
                        if (!ComparePixel(m_pixelData, prevIdx, current, byteDepth))
                            continue;

                        // To see how long can it match, starting at the next pixel
                        int searchForwardIdx = current + 1;
                        int curPrevIdx = prevIdx + 1;
                        while (searchForwardIdx != maxMatchIdx && ComparePixel(m_pixelData, curPrevIdx, searchForwardIdx, byteDepth))
                        {
                            searchForwardIdx++;
                            curPrevIdx++;
                        }

                        int weight = curPrevIdx - prevIdx;
                        if (weight > pos.matchLen)
                        {
                            pos.shiftIdx = i;
                            pos.matchLen = weight;
                        }
                    }
                }
                
                // Determine run mode
                if (pos.matchLen > ignoreThreshold)//Copy Run
                {
                    // This mode will write some cmd byte(s), before that, we need to clear the m_buffer if needed
                    Flush(ref bw);

                    // This maxAllowedCount is
                    // 3 + 7(rc8) and 1 + 3(rct)
                    // The countBase is the default pixel count that decompressor will added to count
                    // When copy count is less than x(x: 3 in rct and 7 in rc8), it will be stored in cmd byte
                    // countMask is used to extract this number inside cmd byte
                    // 
                    // copy command (rct):        copy command (rc8):
                    // 1 sssss cc                 1 ssss ccc
                    // +--------+                 +--------+
                    //    1byte                      1byte
                    // s: shift  c: count 
                    // rct is using a bigger shift table so it needs one more bit to store position information
                    int countThreshold = countBase + countMask;

                    int cmd = (pos.shiftIdx << shiftStart) | 0x80;
                    if (pos.matchLen >= countThreshold)
                    {
                        cmd |= countMask;
                        bw.Write((byte)cmd);
                        bw.Write((ushort)(pos.matchLen - countThreshold));
                    }
                    else
                    {
                        cmd |= pos.matchLen - countBase;
                        bw.Write((byte)cmd);
                    }
                    
                    current += pos.matchLen;
                }
                else//Read Run
                {
                    if (m_bufferSize == maxThunkSize)
                    {
                        Flush(ref bw);
                    }

                    for (int i = 0; i < byteDepth; i++)
                    {
                        m_buffer.Add(m_pixelData[current * byteDepth + i]);
                    }
                    current++;
                    m_bufferSize++;
                }
            }
            Flush(ref bw);

            m_rctRawData = ms.ToArray();
        }

        void Flush(ref BinaryWriter bw)
        {
            if (m_buffer.Count > 0)
            {
                if (m_bufferSize > 0x7f)
                {
                    bw.Write((byte)0x7f);
                    bw.Write((ushort)(m_bufferSize - 0x80));
                }
                else
                {
                    bw.Write((byte)(m_bufferSize - 1));
                }

                foreach (var b in m_buffer)
                {
                    bw.Write(b);
                }

                m_buffer.Clear();
                m_bufferSize = 0;
            }
        }

        static bool ComparePixel(byte[] pixelData, int indexA, int indexB, int byteDepth)
        {
            indexA *= byteDepth;
            indexB *= byteDepth;
            for(int i = 0; i < byteDepth; i++)
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
                -13, -29, -45,  36,  20,   4, -12, -28
            };
        }

        public override bool FromRCT(ref BinaryReader br, DelegateDoCrypt? crypt = null) 
        {
            var isEnc = br.ReadByte() == 0x53;//TC or TS
            Trace.Assert(br.ReadByte() == 0x30);
            var version = br.ReadByte() - 0x30;
            if (version != 0)//Fixme: version == 1
                throw new Exception("Invalid RCT version!");

            m_width = br.ReadUInt32();
            m_height = br.ReadUInt32();
            var dataSize = br.ReadInt32();
            Trace.Assert(dataSize > 0);
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
            Trace.Assert(m_pixelData != null);
            if (m_pixelData == null)
            {
                throw new Exception();
            }

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

        public override byte[] GetRCIamge(DelegateDoCrypt? crypt = null)
        {
            Trace.Assert(m_rctRawData != null);
            if (m_rctRawData == null)
            {
                throw new Exception();
            }

            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(0x9A925A98);//六丁

            if (crypt != null)
            {
                bw.Write(0x30305354);//TS00
                crypt(ref m_rctRawData);
            }
            else
            {
                bw.Write(0x30304354);//TC00
            }
            
            bw.Write(m_width);
            bw.Write(m_height);
            bw.Write(m_rctRawData.Length);
            bw.Write(m_rctRawData);
            return ms.ToArray();
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
                -15, -31, -47,  34,  18,   2, -14, -30
            };
        }

        public override bool FromRCT(ref BinaryReader br, DelegateDoCrypt? crypt = null)
        {
            br.BaseStream.Position += 3;//Seems no encryption

            m_width = br.ReadUInt32();
            m_height = br.ReadUInt32();
            var dataSize = br.ReadInt32();
            var paletteData = br.ReadBytes(0x300);
            Trace.Assert(paletteData.Length == 0x300);

            m_palette = new Rgb24[256];
            for(int i = 0; i < 256; i++)
                m_palette[i] = new Rgb24(paletteData[i*3 + 1], paletteData[i*3 + 1], paletteData[i*3]);

            Trace.Assert(dataSize > 0);
            m_rctRawData = br.ReadBytes(dataSize);
            Unpack();
            return true;
        }

        public override Image<Rgb24> GetImage()
        {
            Trace.Assert(m_palette != null);
            Trace.Assert(m_pixelData != null);
            if (m_pixelData == null || m_palette == null)
            {
                throw new Exception();
            }

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

        public override byte[] GetRCIamge(DelegateDoCrypt? crypt = null)
        {
            Trace.Assert(m_rctRawData != null);
            if (m_rctRawData == null)
            {
                throw new Exception();
            }

            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(0x9A925A98);//六丁
            bw.Write(0x30305F38);//8_00
            bw.Write(m_width); 
            bw.Write(m_height);
            bw.Write(m_rctRawData.Length);
            for(int i = 0; i < 256; i++)//palette
            {
                bw.Write((byte)i);
                bw.Write((byte)i);
                bw.Write((byte)i);
            }
            bw.Write(m_rctRawData); 
            return ms.ToArray();
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
            Trace.Assert(Crc32.Table.Length == 0x100);
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

            var magic = br.ReadUInt32();
            Trace.Assert(magic == 0x9A925A98);//六丁

            var type = br.ReadByte();
            if (type == 0x38)//'8' -> RC8
            {
                var ret = new RC8Image();
                ret.FromRCT(ref br);

                return ret;
            }
            else if(type == 0x54) // 'T' -> RCT
            {
                var ret = new RCTImage();
                ret.FromRCT(ref br, new DelegateDoCrypt(DoCrypt));

                return ret;
            }
            else
            {
                throw new Exception($"Unknown Image Format: {type}");
            }
        }

        public void RCToPNG(string path)
        {
            var rc8ImagePath = path.Replace(".rct", "_.rc8", StringComparison.OrdinalIgnoreCase);
            if(path != rc8ImagePath && File.Exists(rc8ImagePath))
            {
                BlendRCTImages(OpenRCImage(path) as RCTImage, OpenRCImage(rc8ImagePath) as RC8Image).SaveAsPng(Path.ChangeExtension(path, "png"));
            }
            else
            {
                OpenRCImage(path).GetImage().SaveAsPng(Path.ChangeExtension(path, "png"));
            }
        }

        public void PNGToRC(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var image = Image.Load(fs);

            // TODO: Add Encryption
            switch (image.PixelType.BitsPerPixel)
            {
                case 24:
                {
                    var imageData = image.CloneAs<Rgb24>();
                    var rctImage = new RCTImage();
                    var pixels = new byte[imageData.Width * imageData.Height * 3];

                    for (int y = 0; y < imageData.Height; y++)
                    {
                        for (int x = 0; x < imageData.Width; x++)
                        {
                            var index = (y * imageData.Width + x) * 3;
                            var pixel = imageData[x, y];
                            pixels[index + 2] = pixel.R;
                            pixels[index + 1] = pixel.G;
                            pixels[index + 0] = pixel.B;
                        }
                    }
                    rctImage.FromPixels(pixels, (uint)imageData.Width, (uint)imageData.Height);
                    File.WriteAllBytes(Path.ChangeExtension(path, "rct"), rctImage.GetRCIamge());
                    break;
                }
                case 32:
                {
                    var imageData = image.CloneAs<Rgba32>();
                    var rctImage = new RCTImage();
                    var rc8Image = new RC8Image();
                    var pixels = new byte[imageData.Width * imageData.Height * 3];
                    var pixels_8 = new byte[imageData.Width * imageData.Height];

                    for (int y = 0; y < imageData.Height; y++)
                    {
                        for (int x = 0; x < imageData.Width; x++)
                        {
                            var index = (y * imageData.Width + x);
                            var pixel = imageData[x, y];
                            pixels_8[index] = (byte)~pixel.A;
                            index *= 3;
                            pixels[index + 2] = pixel.R;
                            pixels[index + 1] = pixel.G;
                            pixels[index + 0] = pixel.B;
                        }
                    }
                    rctImage.FromPixels(pixels, (uint)imageData.Width, (uint)imageData.Height);
                    rc8Image.FromPixels(pixels_8, (uint)imageData.Width, (uint)imageData.Height);
                    File.WriteAllBytes(Path.ChangeExtension(path, "rct"), rctImage.GetRCIamge());
                    File.WriteAllBytes(path.Replace(".png", "_.rc8", StringComparison.OrdinalIgnoreCase), rc8Image.GetRCIamge());
                    break;
                }
                default:
                    throw new Exception("Unsuppored bpp");
            }
        }

        static Image<Bgra32> BlendRCTImages(RCTImage? rgbImage, RC8Image? alphaImage)
        {
            Trace.Assert(rgbImage != null && alphaImage != null);
            if (rgbImage == null || alphaImage == null)
            {
                throw new Exception();
            }
            Trace.Assert(rgbImage.m_height == alphaImage.m_height && rgbImage.m_width == alphaImage.m_width);
            Trace.Assert(alphaImage.m_palette != null);
            Trace.Assert(alphaImage.m_pixelData != null);
            Trace.Assert(rgbImage.m_pixelData != null);
            if (alphaImage.m_palette == null || alphaImage.m_pixelData == null || rgbImage.m_pixelData == null)
            {
                throw new Exception();
            }

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