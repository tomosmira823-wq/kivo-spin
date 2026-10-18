using ImageMagick;

namespace KivoSpin.Core
{
    public class EditSpec
    {
        public int Rotation;          // 0, 90, 180, 270 (clockwise)
        public bool FlipH, FlipV;
        public int Brightness;        // -100..100
        public int Contrast;          // -100..100
        public int Saturation;        // -100..100
        public bool Grayscale;
        public int ResizeW, ResizeH;  // 0 = keep

        public bool IsIdentity => Rotation % 360 == 0 && !FlipH && !FlipV && Brightness == 0 && Contrast == 0 &&
                                  Saturation == 0 && !Grayscale && ResizeW <= 0 && ResizeH <= 0;
    }

    public class ToolResult
    {
        public List<string> Outputs = new List<string>();
        public string Note;
    }

    /// <summary>The "TOOLS" side of the wheel (non-interactive parts).</summary>
    public static class Tools
    {
        // ----------------------------------------------------------- compress

        public static ToolResult Compress(string src, Func<string, string, bool> ask, Action<int, int> progress)
        {
            var result = new ToolResult();
            long before = new FileInfo(src).Length;

            if (FileKinds.Of(src) == FileKind.Pdf)
            {
                var outPath = PdfTools.CompressLossless(src);
                long after = new FileInfo(outPath).Length;
                if (after > before * 0.9)
                {
                    bool strong = ask != null && ask("Make it much smaller?",
                        "This PDF is already tight. Kivo Spin can shrink it a lot more by turning each page into an image, but the text won't be selectable or searchable afterwards.");
                    AppPaths.TryDelete(outPath);
                    if (!strong) { result.Note = "Already about as small as it gets without flattening pages."; return result; }
                    outPath = PdfTools.CompressStrong(src, progress);
                    after = new FileInfo(outPath).Length;
                    if (after >= before) { AppPaths.TryDelete(outPath); result.Note = "Couldn't make this PDF any smaller."; return result; }
                }
                result.Outputs.Add(outPath);
                result.Note = $"{Imaging.HumanSize(before)} → {Imaging.HumanSize(after)}";
                return result;
            }

            using var img = Imaging.Load(src);
            Imaging.FitInside(img, AppSettings.Current.CompressMaxSide);
            img.Strip();

            string fmt = FileKinds.FormatId(src);
            string target;
            switch (fmt)
            {
                case "jpg": case "webp": case "heic": case "avif": target = fmt; break;
                case "png": case "gif": case "ico": target = fmt == "png" ? "png" : fmt; break;
                default: target = img.HasAlpha ? "png" : "jpg"; break;
            }

            var output = AppPaths.Output(src, target, "compressed");
            if (target == "png")
            {
                img.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                img.Write(output, MagickFormat.Png8);
                try { new ImageOptimizer { OptimalCompression = true }.LosslessCompress(output); } catch { }
            }
            else
            {
                Imaging.SaveAs(img, target, output, AppSettings.Current.CompressQuality);
            }

            long size = new FileInfo(output).Length;
            if (size >= before)
            {
                AppPaths.TryDelete(output);
                result.Note = Path.GetFileName(src) + " is already as small as it gets.";
                return result;
            }
            result.Outputs.Add(output);
            result.Note = $"{Imaging.HumanSize(before)} → {Imaging.HumanSize(size)}";
            return result;
        }

        // ----------------------------------------------------------- metadata

        public static string StripMetadata(string src)
        {
            if (FileKinds.Of(src) == FileKind.Pdf)
                return PdfTools.WriteInfo(src, null, null, null, null, stripAll: true);

            using var img = Imaging.Load(src); // AutoOrient bakes rotation in before EXIF is removed
            img.Strip();
            return Imaging.SaveSameFormat(img, src, "clean");
        }

        public static List<(string key, string value)> ReadImageMetadata(string src, out bool hasGps)
        {
            hasGps = false;
            var rows = new List<(string, string)>();
            using var img = new MagickImage(src);
            rows.Add(("Format", img.Format.ToString()));
            rows.Add(("Size", $"{img.Width} × {img.Height} px"));
            rows.Add(("Colour space", img.ColorSpace.ToString()));
            rows.Add(("File size", Imaging.HumanSize(new FileInfo(src).Length)));

            var exif = img.GetExifProfile();
            if (exif != null)
            {
                foreach (var v in exif.Values)
                {
                    string tag = v.Tag.ToString();
                    if (tag.StartsWith("GPS", StringComparison.OrdinalIgnoreCase)) hasGps = true;
                    rows.Add((tag, Describe(v.GetValue())));
                }
            }
            if (img.GetIptcProfile() != null) rows.Add(("IPTC", "Present"));
            if (img.GetXmpProfile() != null) rows.Add(("XMP", "Present"));
            if (img.GetColorProfile() != null) rows.Add(("Colour profile", img.GetColorProfile().Description ?? "Embedded"));
            return rows;
        }

        private static string Describe(object value)
        {
            if (value == null) return "";
            if (value is byte[] bytes) return bytes.Length > 16 ? $"({bytes.Length} bytes)" : BitConverter.ToString(bytes);
            if (value is Array arr)
            {
                var parts = new List<string>();
                foreach (var o in arr) { parts.Add(o?.ToString() ?? ""); if (parts.Count >= 6) { parts.Add("…"); break; } }
                return string.Join(", ", parts);
            }
            var s = value.ToString() ?? "";
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        // ----------------------------------------------------------- edit

        public static void ApplyEdit(IMagickImage<byte> img, EditSpec e, bool includeResize, double previewScale = 1.0)
        {
            int rot = ((e.Rotation % 360) + 360) % 360;
            if (rot != 0) img.Rotate(rot);
            if (e.FlipH) img.Flop();
            if (e.FlipV) img.Flip();
            if (e.Brightness != 0 || e.Contrast != 0)
                img.BrightnessContrast(new Percentage(e.Brightness), new Percentage(e.Contrast));
            if (e.Grayscale || e.Saturation != 0)
                img.Modulate(new Percentage(100), new Percentage(e.Grayscale ? 0 : 100 + e.Saturation), new Percentage(100));
            if (includeResize && (e.ResizeW > 0 || e.ResizeH > 0))
            {
                int w = e.ResizeW > 0 ? e.ResizeW : (int)Math.Round(img.Width * (double)e.ResizeH / img.Height);
                int h = e.ResizeH > 0 ? e.ResizeH : (int)Math.Round(img.Height * (double)e.ResizeW / img.Width);
                img.Resize(new MagickGeometry((uint)Math.Max(1, w), (uint)Math.Max(1, h)) { IgnoreAspectRatio = true });
            }
        }

        public static string SaveEdited(string src, EditSpec e)
        {
            using var img = Imaging.Load(src);
            ApplyEdit(img, e, includeResize: true);
            return Imaging.SaveSameFormat(img, src, "edited");
        }

        // ----------------------------------------------------------- crop

        public static string CropImage(string src, int x, int y, int w, int h)
        {
            using var img = Imaging.Load(src);
            x = Math.Clamp(x, 0, (int)img.Width - 1);
            y = Math.Clamp(y, 0, (int)img.Height - 1);
            w = Math.Clamp(w, 1, (int)img.Width - x);
            h = Math.Clamp(h, 1, (int)img.Height - y);
            img.Crop(new MagickGeometry(x, y, (uint)w, (uint)h));
            img.ResetPage();
            return Imaging.SaveSameFormat(img, src, "cropped");
        }

        // ----------------------------------------------------------- backgrounds

        public static string AddBackground(string src, string hexColor, string bgImagePath, Action<string> status)
        {
            using var img = Imaging.Load(src);
            if (!img.HasAlpha || IsFullyOpaque(img))
            {
                status?.Invoke("Cutting out the subject first…");
                BackgroundRemover.Cutout(img, status);
            }

            MagickImage bg;
            if (!string.IsNullOrEmpty(bgImagePath) && File.Exists(bgImagePath))
            {
                bg = Imaging.Load(bgImagePath);
                bg.Resize(new MagickGeometry(img.Width, img.Height) { FillArea = true });
                bg.Crop(img.Width, img.Height, Gravity.Center);
                bg.ResetPage();
            }
            else
            {
                bg = new MagickImage(new MagickColor(string.IsNullOrWhiteSpace(hexColor) ? "#FFFFFF" : hexColor), img.Width, img.Height);
            }

            using (bg)
            {
                bg.Composite(img, CompositeOperator.Over);
                bg.Alpha(AlphaOption.Off);
                var target = Imaging.SameFormatTarget(src);
                if (target == "ico" || target == "gif") target = "png";
                var outPath = AppPaths.Output(src, target, "new bg");
                Imaging.SaveAs(bg, target, outPath, AppSettings.Current.Quality);
                return outPath;
            }
        }

        private static bool IsFullyOpaque(IMagickImage<byte> img)
        {
            try
            {
                var stats = img.Statistics().GetChannel(PixelChannel.Alpha);
                if (stats == null) return true;
                return stats.Minimum >= 250; // Q8: 0..255
            }
            catch { return false; }
        }
    }
}
