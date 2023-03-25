using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajiroRC
{
    public delegate bool DoCryptDelegate(byte[] data);
    abstract class RCImage
    {
        public int m_width;
        public int m_height;
        public int m_offsetX;
        public int m_offsetY;
        public int m_bpp;
        public byte[] m_pixelData = Array.Empty<byte>();
        public byte[] m_rctRawData = Array.Empty<byte>();
        public sbyte[] m_shiftTable = Array.Empty<sbyte>();

        public abstract void FromRCT(BinaryReader br, DoCryptDelegate crypt);
        public void FromPixels(byte[] pixels, int width, int height)
        {
            m_width = width;
            m_height = height;
            m_pixelData = pixels;
            Pack();
        }

        public abstract Image<Bgr24> GetImage();

        public abstract byte[] GetRCIamge(DoCryptDelegate crypt, bool encrypt);

        protected void Unpack()
        {
            if(m_rctRawData.Length == 0) 
            {
                return;
            }

            int byteDepth = m_bpp / 8;
            int countMask = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            int shiftStart = 3 - (m_shiftTable.Length >> 5);
            int countBase = byteDepth == 1 ? 3 : 1;

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
                    copySourceAdvance = (copySourceAdvance >> 4) - (shiftRow * m_width);

                    copySourceAdvance *= byteDepth;

                    if (copySourceAdvance >= 0 || dstPos + copySourceAdvance < 0)
                        throw new Exception("Decompress error: Shift param incorrect");

                    Utils.Binary.CopyOverlapped(m_pixelData, dstPos + copySourceAdvance, dstPos, count);
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
            if(m_pixelData.Length == 0)
            {
                return;
            }

            const int maxThunkSize = 0xffff + 0x7F;
            const int maxMatchSize = 0xffff + 1;
            int byteDepth = m_bpp / 8;
            int countMask = 16 + (sbyte)~(8 + ((m_shiftTable.Length >> 5) * 4));
            int shiftStart = 3 - (m_shiftTable.Length >> 5);
            int countBase = byteDepth == 1 ? 3 : 1;
            int ignoreThreshold = byteDepth == 1 ? 2 : 0;

            m_buffer.Capacity = maxThunkSize;
            m_buffer.Clear();
            m_bufferSize = 0;

            var ms = new MemoryStream
            {
                Capacity = Convert.ToInt32(m_width * m_height * byteDepth)
            };
            var bw = new BinaryWriter(ms);

            //InitShiftTable
            var shiftTable = new int[m_shiftTable.Length];
            for (int i = 0; i < m_shiftTable.Length; ++i)
            {
                int shift = m_shiftTable[i];
                int shiftRow = shift & 0x0f;
                shiftTable[i] = (shift >> 4) - (shiftRow * m_width);
            }

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
                    Flush(bw);

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
                        Flush(bw);
                    }

                    for (int i = 0; i < byteDepth; i++)
                    {
                        m_buffer.Add(m_pixelData[current * byteDepth + i]);
                    }
                    current++;
                    m_bufferSize++;
                }
            }
            Flush(bw);

            m_rctRawData = ms.ToArray();
        }

        void Flush(BinaryWriter bw)
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
            for (int i = 0; i < byteDepth; i++)
            {
                if (pixelData[indexA + i] != pixelData[indexB + i])
                    return false;
            }
            return true;
        }
    }
}
