using System.Diagnostics;
using System.Text;
using static MajiroRC.Utils;
using SixLabors.ImageSharp.Advanced;
using System.Runtime.InteropServices;

namespace MajiroRC
{
    class RCFile
    {
        byte[] m_imgKey = Array.Empty<byte>();

        public void SetPassword(string password)
        {
            byte[] bin_pass = Encoding.GetEncoding("shift_jis").GetBytes(password);
            uint crc32 = Crc32.Compute(bin_pass, 0, bin_pass.Length);
            m_imgKey = new byte[0x400];

            for (uint i = 0; i < 0x100; ++i)
                Binary.WriteUint32LE(m_imgKey, i * 4, crc32 ^ Crc32.Table[(i + crc32) & 0xFF]);
        }

        bool DoCrypt(byte[] data)
        {
            if (m_imgKey.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= m_imgKey[i & 0x3FF];
            }
            return true;
        }

        public RCImage OpenRCImage(string path)
        {
            var fs = File.OpenRead(path);
            var br = new BinaryReader(fs);

            var magic = br.ReadUInt32();
            Trace.Assert(magic == 0x9A925A98);//六丁

            var type = br.ReadByte();
            if (type == 0x38)//'8' -> RC8
            {
                var ret = new RC8Image();
                ret.FromRCT(br, new DoCryptDelegate(DoCrypt));

                return ret;
            }
            else if (type == 0x54) // 'T' -> RCT
            {
                var ret = new RCTImage();
                ret.FromRCT(br, new DoCryptDelegate(DoCrypt));

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
            if (path != rc8ImagePath && File.Exists(rc8ImagePath))
            {
                BlendRCTImages(OpenRCImage(path) as RCTImage, OpenRCImage(rc8ImagePath) as RC8Image).SaveAsPng(Path.ChangeExtension(path, "png"));
            }
            else
            {
                OpenRCImage(path).GetImage().SaveAsPng(Path.ChangeExtension(path, "png"));
            }
        }

        public void PNGToRC(string path, bool encrypt = false)
        {
            var fs = File.OpenRead(path);
            var image = Image.Load(fs);

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
                    
                    rctImage.FromPixels(pixels, imageData.Width, imageData.Height);
                    File.WriteAllBytes(Path.ChangeExtension(path, "rct"), rctImage.GetRCIamge(new DoCryptDelegate(DoCrypt), encrypt));
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
                    rctImage.FromPixels(pixels, imageData.Width, imageData.Height);
                    rc8Image.FromPixels(pixels_8, imageData.Width, imageData.Height);
                    File.WriteAllBytes(Path.ChangeExtension(path, "rct"), rctImage.GetRCIamge(new DoCryptDelegate(DoCrypt), encrypt));
                    File.WriteAllBytes(path.Replace(".png", "_.rc8", StringComparison.OrdinalIgnoreCase), rc8Image.GetRCIamge(new DoCryptDelegate(DoCrypt), encrypt));
                    break;
                }
                default:
                    throw new Exception("Unsuppored bpp");
            }
        }

        static Image<Bgra32> BlendRCTImages(RCTImage? rgbImage, RC8Image? alphaImage)
        {
            ArgumentNullException.ThrowIfNull(rgbImage);
            ArgumentNullException.ThrowIfNull(alphaImage);

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
            return Image.LoadPixelData<Bgra32>(pixelData, rgbImage.m_width, rgbImage.m_height);
        }
    }
}
