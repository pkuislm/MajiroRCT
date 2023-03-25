using System;

namespace MajiroRC
{
    internal class Utils
    {
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

            public static void WriteUint32LE(byte[] dst, uint pos, uint val)
            {
                dst[pos + 3] = (byte)(val >> 24);
                dst[pos + 2] = (byte)(val >> 16);
                dst[pos + 1] = (byte)(val >> 8);
                dst[pos + 0] = (byte)(val);
            }
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
}
