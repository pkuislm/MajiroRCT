using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajiroRC
{
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

        public override void FromRCT(BinaryReader br, DoCryptDelegate crypt)
        {
            var isEnc = br.ReadByte() == 0x53;//TC or TS
            Trace.Assert(br.ReadByte() == 0x30);
            var version = br.ReadByte() - 0x30;
            if (version != 0)//Fixme: version == 1
                throw new Exception("Invalid RCT version!");

            m_width = br.ReadInt32();
            m_height = br.ReadInt32();
            var dataSize = br.ReadInt32();
            Trace.Assert(dataSize > 0);
            m_rctRawData = br.ReadBytes(dataSize);

            if (isEnc == true)
            {
                if (!crypt(m_rctRawData))
                    throw new Exception("RCT is encrypted, please offer a key by adding '-k [password]' in args.");
            }
            Unpack();
        }

        public override Image<Bgr24> GetImage()
        {
            return m_pixelData.Length > 0 ? Image.LoadPixelData<Bgr24>(m_pixelData, m_width, m_height) : new Image<Bgr24>(1, 1);
        }

        public override byte[] GetRCIamge(DoCryptDelegate crypt, bool encrypt)
        {
            if(m_rctRawData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var ms = new MemoryStream
            {
                Capacity = m_rctRawData.Length + sizeof(int) * 5,
            };
            var bw = new BinaryWriter(ms);

            bw.Write(0x9A925A98);//六丁

            if (encrypt && crypt(m_rctRawData))
            {
                bw.Write(0x30305354);//TS00
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
}
