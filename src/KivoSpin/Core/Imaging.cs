using System.Windows.Media.Imaging;
using ImageMagick;

namespace KivoSpin.Core
{
    /// <summary>Image loading/saving helpers built on Magick.NET, plus WPF bitmap bridges.</summary>
    public static class Imaging
    {
        private static readonly HashSet<string> Writable = new HashSet<string>
        {
            "png", "jpg", "webp", "heic", "avif", "tiff", "bmp", "gif", "ico"
        };

        public static MagickImage Load(string path)
        {
            try
            {
                var img = new MagickImage(path);
                img.AutoOrient();
                return img;
            }
            catch (MagickException ex)
            {
                Log.Error("Image load failed: " + path, ex);
                throw new UserFacingException("Couldn't open this image (" + Path.GetExtension(path) + "). It may be damaged or a format this PC can't read.");
            }
        }

        public static MagickImage LoadPreview(string path, int maxSide)
        {
            var img = Load(path);
            FitInside(img, maxSide);
            return img;
        }

        public static void FitInside(IMagickImage<byte> img, int maxSide)
        {
            if (maxSide > 0 && Math.Max(img.Width, img.Height) > maxSide)
                img.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide)); // keeps aspect ratio
        }

        public static void FlattenOnWhite(IMagickImage<byte> img)
        {
            if (img.HasAlpha)
            {
                img.BackgroundColor = MagickColors.White;
                img.Alpha(AlphaOption.Remove);
            }
        }

        /// <summary>Writes img to outPath in the given wheel format.</summary>
        public static void SaveAs(IMagickImage<byte> img, string target, string outPath, int quality)
        {
            uint q = (uint)Math.Clamp(quality, 1, 100);
            try
            {
                switch (target)
                {
                    case "jpg":
                        FlattenOnWhite(img);
                        img.Quality = q;
                        img.Write(outPath, MagickFormat.Jpeg);
                        break;
                    case "png":
                        img.Write(outPath, MagickFormat.Png);
                        break;
                    case "webp":
                        img.Quality = q;
                        img.Write(outPath, MagickFormat.WebP);
                        break;
                    case "heic":
                        img.Quality = q;
                        img.Write(outPath, MagickFormat.Heic);
                        break;
                    case "avif":
                        img.Quality = q;
                        img.Write(outPath, MagickFormat.Avif);
                        break;
                    case "tiff":
                        img.Settings.Compression = CompressionMethod.LZW;
                        img.Write(outPath, MagickFormat.Tiff);
                        break;
                    case "bmp":
                        img.Write(outPath, MagickFormat.Bmp);
                        break;
                    case "gif":
                        img.Write(outPath, MagickFormat.Gif);
                        break;
                    case "ico":
                        MakeIcon(img);
                        img.Write(outPath, MagickFormat.Ico);
                        break;
                    default:
                        img.Write(outPath, MagickFormat.Png);
                        break;
                }
            }
            catch (MagickException ex)
            {
                AppPaths.TryDelete(outPath);
                Log.Error("Write failed: " + target, ex);
                throw new UserFacingException("Couldn't save as " + target.ToUpperInvariant() + " on this PC.");
            }
        }

        private static void MakeIcon(IMagickImage<byte> img)
        {
            if (!img.HasAlpha) img.Alpha(AlphaOption.Opaque);
            uint side = Math.Max(img.Width, img.Height);
            img.BackgroundColor = MagickColors.Transparent;
            img.Extent(side, side, Gravity.Center);
            if (side > 256) img.Resize(256, 256);
            img.Settings.SetDefine("icon:auto-resize", "256,128,64,48,32,16");
        }

        /// <summary>The format a result should keep when "editing" a file (formats we can't write fall back to PNG).</summary>
        public static string SameFormatTarget(string sourcePath)
        {
            var id = FileKinds.FormatId(sourcePath);
            return Writable.Contains(id) ? id : "png";
        }

        public static string SaveSameFormat(IMagickImage<byte> img, string sourcePath, string suffix)
        {
            var target = SameFormatTarget(sourcePath);
            var outPath = AppPaths.Output(sourcePath, target, suffix);
            SaveAs(img, target, outPath, AppSettings.Current.Quality);
            return outPath;
        }

        // ----------------------------------------------------- WPF bridges

        public static BitmapSource ToBitmapSource(IMagickImage<byte> img)
        {
            using var ms = new MemoryStream();
            img.Write(ms, MagickFormat.Png);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public static byte[] ToPng(BitmapSource bs)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bs));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static MagickImage FromBitmapSource(BitmapSource bs) => new MagickImage(ToPng(bs));

        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.##") + " MB";
        }
    }
}
