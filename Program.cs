using System.Diagnostics;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MajiroRC
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var rcFile = new RCFile();

            if(args.Length < 2)
            {
                Console.WriteLine("RCImageTool v0.2.\nUsage: \nExtract: RCImage.exe [-k [key]] -e [RCFiles]\nPack: RCImage.exe [-c -k [key]] -p [PNGFiles]");
                return;
            }

            List<string> extractList = new List<string>();
            List<string> packList = new List<string>();
            bool encrypt = false;
            for(int i = 0; i < args.Length;)
            {
                switch (args[i])
                {
                    case "-c":
                        i++;
                        encrypt = true;
                        break;
                    case "-k":
                        rcFile.SetPassword(args[++i]);
                        i++;
                        break;
                    case "-e":
                        while (++i < args.Length)
                        {
                            if (args[i][0] == '-')
                                break;
                            extractList.Add(args[i]);
                        }
                        break;
                    case "-p":
                        while (++i < args.Length)
                        {
                            if (args[i][0] == '-')
                                break;
                            packList.Add(args[i]);
                        }
                        break;
                    default:
                        Console.WriteLine($"Unknown arg: \"{args[i]}\"");
                        break;
                }
            }

            foreach (var extract in extractList)
            {
                Console.WriteLine($"Converting {extract} to PNG...");
                try
                {
                    rcFile.RCToPNG(extract);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            foreach (var pack in packList)
            {
                Console.WriteLine($"Converting {pack} to RC...");
                try
                {
                    rcFile.PNGToRC(pack, encrypt);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }
    }
}