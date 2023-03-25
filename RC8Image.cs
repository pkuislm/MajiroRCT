using System;
using System.IO;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MajiroRC
{
    class RC8Image : RCImage
    {
        public Bgr24[] m_palette = Array.Empty<Bgr24>();

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

        public override void FromRCT(BinaryReader br, DoCryptDelegate crypt)
        {
            br.BaseStream.Position += 3;//Seems no encryption

            m_width = br.ReadInt32();
            m_height = br.ReadInt32();
            var dataSize = br.ReadInt32();
            var paletteData = br.ReadBytes(0x300);
            Trace.Assert(paletteData.Length == 0x300);

            m_palette = new Bgr24[256];
            for (int i = 0; i < 256; i++)
                m_palette[i] = new Bgr24(paletteData[i * 3 + 1], paletteData[i * 3 + 1], paletteData[i * 3]);

            Trace.Assert(dataSize > 0);
            m_rctRawData = br.ReadBytes(dataSize);
            Unpack();
        }

        public override Image<Bgr24> GetImage()
        {
            if(m_pixelData.Length == 0 || m_palette.Length == 0)
            {
                return new Image<Bgr24>(1, 1);
            }

            var img = new Image<Bgr24>(m_width, m_height);

            for (int y = 0; y < m_height; y++)
            {
                for (int x = 0; x < m_width; x++)
                {
                    img[x, y] = m_palette[m_pixelData[y * m_width + x]];
                }
            }

            return img;
        }

        public override byte[] GetRCIamge(DoCryptDelegate crypt, bool encrypt)
        {
            if(m_rctRawData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var ms = new MemoryStream
            {
                Capacity = m_rctRawData.Length + 3 * 256 + sizeof(int) * 5,
            };
            var bw = new BinaryWriter(ms);

            bw.Write(0x9A925A98);//六丁
            bw.Write(0x30305F38);//8_00
            bw.Write(m_width);
            bw.Write(m_height);
            bw.Write(m_rctRawData.Length);
            for (int i = 0; i < 256; i++)//palette
            {
                bw.Write((byte)i);
                bw.Write((byte)i);
                bw.Write((byte)i);
            }
            bw.Write(m_rctRawData);
            return ms.ToArray();
        }
    }
}
