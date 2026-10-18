using System.Windows.Media.Imaging;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace KivoSpin.Core
{
    /// <summary>PDF building and editing with PDFsharp.</summary>
    public static class PdfTools
    {
        public const double A4LongSidePt = 842;

        public static PdfDocument OpenImport(string path) => Open(path, PdfDocumentOpenMode.Import);
        public static PdfDocument OpenModify(string path) => Open(path, PdfDocumentOpenMode.Modify);

        private static PdfDocument Open(string path, PdfDocumentOpenMode mode)
        {
            try { return PdfReader.Open(path, mode); }
            catch (Exception ex) { throw PdfRender.Friendly(ex); }
        }

        /// <summary>Encodes img in a form PDFsharp's importer accepts (tries several simple encodings).</summary>
        public static XImage MakeXImage(IMagickImage<byte> img, List<IDisposable> keepAlive, bool keepAlpha, int jpegQuality = 90)
        {
            // PDFsharp's own importer is picky: give it plain 8-bit sRGB data, trying the
            // simplest encodings in turn until one loads.
            XImage ximg = null;
            var attempts = keepAlpha
                ? new[] { MagickFormat.Png32, MagickFormat.Bmp, MagickFormat.Jpeg }
                : new[] { MagickFormat.Jpeg, MagickFormat.Png24, MagickFormat.Bmp3 };
            Exception last = null;
            foreach (var fmt in attempts)
            {
                var ms = new MemoryStream();
                try
                {
                    using (var copy = img.Clone())
                    {
                        copy.ColorSpace = ColorSpace.sRGB;
                        copy.Depth = 8;
                        copy.Strip();
                        if (fmt == MagickFormat.Jpeg || fmt == MagickFormat.Png24 || fmt == MagickFormat.Bmp3)
                            Imaging.FlattenOnWhite(copy);
                        if (fmt == MagickFormat.Jpeg) copy.Quality = (uint)Math.Clamp(jpegQuality, 1, 100);
                        if (fmt == MagickFormat.Bmp) copy.Settings.SetDefine(MagickFormat.Bmp, "format", "bmp4");
                        copy.Write(ms, fmt);
                    }
                    ms.Position = 0;
                    ximg = XImage.FromStream(ms);
                    keepAlive.Add(ms);
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    ms.Dispose();
                }
            }
            if (ximg == null)
            {
                Log.Error("No image encoding accepted by PDF writer", last);
                throw new UserFacingException("Couldn't put this image into a PDF.");
            }
            keepAlive.Add(ximg);
            return ximg;
        }

        /// <summary>Adds a page showing the image. If wPt/hPt are 0 the page is sized from the image (long side ≤ A4).</summary>
        public static PdfPage AddImagePage(PdfDocument doc, IMagickImage<byte> img, List<IDisposable> keepAlive,
            double wPt = 0, double hPt = 0, bool forceJpeg = false, int jpegQuality = 90)
        {
            var ximg = MakeXImage(img, keepAlive, img.HasAlpha && !forceJpeg, jpegQuality);

            if (wPt <= 0 || hPt <= 0)
            {
                wPt = img.Width * 0.75;
                hPt = img.Height * 0.75;
                double s = Math.Min(1.0, A4LongSidePt / Math.Max(wPt, hPt));
                wPt *= s; hPt *= s;
            }

            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(wPt);
            page.Height = XUnit.FromPoint(hPt);
            using (var g = XGraphics.FromPdfPage(page))
                g.DrawImage(ximg, 0, 0, wPt, hPt);
            return page;
        }

        public static PdfPage AddBitmapPage(PdfDocument doc, BitmapSource bmp, double wPt, double hPt, List<IDisposable> keepAlive,
            bool jpeg = true, int quality = 85)
        {
            using var img = Imaging.FromBitmapSource(bmp);
            if (!jpeg) img.Alpha(AlphaOption.Off);
            return AddImagePage(doc, img, keepAlive, wPt, hPt, forceJpeg: jpeg, jpegQuality: quality);
        }

        /// <summary>Builds one PDF from any mix of images, PDFs and Word files (in the given order).</summary>
        public static string BuildPdf(IList<string> files, string outPath, Action<int, int> progress = null)
        {
            var keep = new List<IDisposable>();
            var temps = new List<string>();
            try
            {
                using var doc = new PdfDocument();
                doc.Info.Creator = "Kivo Spin";
                for (int f = 0; f < files.Count; f++)
                {
                    progress?.Invoke(f + 1, files.Count);
                    var file = files[f];
                    switch (FileKinds.Of(file))
                    {
                        case FileKind.Image:
                            using (var frames = new MagickImageCollection(file))
                            {
                                foreach (var frame in frames)
                                {
                                    frame.AutoOrient();
                                    AddImagePage(doc, frame, keep);
                                }
                            }
                            break;
                        case FileKind.Pdf:
                            AppendPdf(doc, file);
                            break;
                        case FileKind.Word:
                            var tmp = AppPaths.NewTempFile("pdf");
                            temps.Add(tmp);
                            OfficeBridge.ToPdf(file, tmp);
                            AppendPdf(doc, tmp);
                            break;
                        default:
                            throw new UserFacingException(Path.GetFileName(file) + " can't go into a PDF.");
                    }
                }
                if (doc.PageCount == 0) throw new UserFacingException("Nothing to put in the PDF.");
                doc.Save(outPath);
                return outPath;
            }
            finally
            {
                foreach (var k in keep) { try { k.Dispose(); } catch { } }
                foreach (var t in temps) AppPaths.TryDelete(t);
            }
        }

        private static void AppendPdf(PdfDocument doc, string pdfPath)
        {
            using var src = OpenImport(pdfPath);
            for (int i = 0; i < src.PageCount; i++)
                doc.AddPage(src.Pages[i]);
        }

        // ----------------------------------------------------------- PDF → images / DOCX

        public static List<string> ToImages(string pdf, string target)
        {
            var outputs = new List<string>();
            int count = PdfRender.PageCount(pdf);
            string folder = count > 1 ? AppPaths.OutputFolderFor(pdf, "pages") : null;
            string baseName = Path.GetFileNameWithoutExtension(pdf);

            PdfRender.RenderPages(pdf, AppSettings.Current.PdfDpi, null, (i, bmp, eff) =>
            {
                using var img = Imaging.FromBitmapSource(bmp);
                img.Density = new Density(eff, eff, DensityUnit.PixelsPerInch);
                string outPath = folder == null
                    ? AppPaths.Output(pdf, target)
                    : AppPaths.Unique(Path.Combine(folder, $"{baseName} p{(i + 1).ToString().PadLeft(count.ToString().Length, '0')}.{target}"));
                Imaging.SaveAs(img, target, outPath, AppSettings.Current.Quality);
                if (folder == null) outputs.Add(outPath);
            });

            if (folder != null) outputs.Add(folder);
            return outputs;
        }

        public static string ToDocx(string pdf, string outPath)
        {
            var blocks = new List<DocBlock>();
            int count = PdfRender.PageCount(pdf);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) blocks.Add(new DocPageBreak());
                var text = PdfRender.PageText(pdf, i);
                if (text.Trim().Length >= 25)
                {
                    foreach (var para in ToParagraphs(text))
                        blocks.Add(new DocText { Text = para });
                }
                else
                {
                    // Scanned or image-only page: keep it as a picture.
                    var bmp = PdfRender.RenderPage(pdf, i, 150);
                    if (bmp != null)
                        blocks.Add(new DocImage { Png = Imaging.ToPng(bmp), W = bmp.PixelWidth, H = bmp.PixelHeight });
                }
            }
            DocxWriter.Write(outPath, blocks);
            return outPath;
        }

        // Joins wrapped PDF lines back into paragraphs.
        private static IEnumerable<string> ToParagraphs(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new System.Text.StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                bool blank = line.Trim().Length == 0;
                bool bullet = line.TrimStart().StartsWith("•") || line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* ")
                              || System.Text.RegularExpressions.Regex.IsMatch(line.TrimStart(), @"^\d+[\.\)]\s");
                if (blank || bullet)
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    if (blank) continue;
                }
                if (sb.Length > 0)
                {
                    var prev = sb.ToString();
                    char last = prev[prev.Length - 1];
                    bool ends = ".:!?;".IndexOf(last) >= 0;
                    if (ends && prev.Length < 90) { yield return prev; sb.Clear(); }
                    else if (prev.EndsWith("-")) { sb.Length -= 1; }
                    else sb.Append(' ');
                }
                sb.Append(line.Trim());
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        // ----------------------------------------------------------- tools

        /// <summary>Lossless re-save with best compression. Returns output path.</summary>
        public static string CompressLossless(string pdf)
        {
            var outPath = AppPaths.Output(pdf, "pdf", "compressed");
            using var doc = OpenModify(pdf);
            doc.Options.NoCompression = false;
            doc.Options.CompressContentStreams = true;
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
            doc.Save(outPath);
            return outPath;
        }

        /// <summary>Re-renders every page as a JPEG. Much smaller, but text is no longer selectable.</summary>
        public static string CompressStrong(string pdf, Action<int, int> progress = null)
        {
            var outPath = AppPaths.Output(pdf, "pdf", "compressed");
            var keep = new List<IDisposable>();
            try
            {
                using var doc = new PdfDocument();
                int count = PdfRender.PageCount(pdf);
                PdfRender.RenderPages(pdf, 120, null, (i, bmp, eff) =>
                {
                    progress?.Invoke(i + 1, count);
                    double wPt = bmp.PixelWidth * 72.0 / eff, hPt = bmp.PixelHeight * 72.0 / eff;
                    AddBitmapPage(doc, bmp, wPt, hPt, keep, jpeg: true, quality: AppSettings.Current.CompressQuality - 8);
                });
                doc.Save(outPath);
                return outPath;
            }
            finally { foreach (var k in keep) { try { k.Dispose(); } catch { } } }
        }

        /// <summary>Applies a crop (fractions of the displayed first page, top-left origin) to every page.</summary>
        public static string Crop(string pdf, double fx0, double fy0, double fx1, double fy1)
        {
            var outPath = AppPaths.Output(pdf, "pdf", "cropped");
            using var doc = OpenModify(pdf);
            foreach (PdfPage page in doc.Pages)
            {
                var box = page.CropBox;
                if (box.IsEmpty || box.Width <= 0 || box.Height <= 0) box = page.MediaBox;
                int rot = ((page.Rotate % 360) + 360) % 360;

                var a = Unrotate(fx0, fy0, rot);
                var b = Unrotate(fx1, fy1, rot);
                double ux0 = Math.Min(a.x, b.x), ux1 = Math.Max(a.x, b.x);
                double uy0 = Math.Min(a.y, b.y), uy1 = Math.Max(a.y, b.y);

                double x1 = box.X1 + ux0 * box.Width, x2 = box.X1 + ux1 * box.Width;
                double y1 = box.Y2 - uy1 * box.Height, y2 = box.Y2 - uy0 * box.Height; // PDF y goes up
                page.CropBox = new PdfRectangle(new XPoint(x1, y1), new XPoint(x2, y2));
            }
            doc.Save(outPath);
            return outPath;
        }

        // Display-space fraction → unrotated page fraction.
        private static (double x, double y) Unrotate(double u, double v, int rot)
        {
            switch (rot)
            {
                case 90: return (v, 1 - u);
                case 180: return (1 - u, 1 - v);
                case 270: return (1 - v, u);
                default: return (u, v);
            }
        }

        public class PdfInfo
        {
            public string Title, Author, Subject, Keywords, Creator, Producer, Created, Modified;
            public int Pages;
            public bool HasXmp;
        }

        public static PdfInfo ReadInfo(string pdf)
        {
            using var doc = OpenImport(pdf);
            var i = doc.Info;
            return new PdfInfo
            {
                Title = i.Title, Author = i.Author, Subject = i.Subject, Keywords = i.Keywords,
                Creator = i.Creator, Producer = i.Producer,
                Created = SafeDate(() => i.CreationDate), Modified = SafeDate(() => i.ModificationDate),
                Pages = doc.PageCount,
                HasXmp = doc.Internals.Catalog.Elements.ContainsKey("/Metadata")
            };
        }

        private static string SafeDate(Func<DateTime> f)
        {
            try { var d = f(); return d == DateTime.MinValue ? "" : d.ToString("d MMM yyyy HH:mm"); }
            catch { return ""; }
        }

        public static string WriteInfo(string pdf, string title, string author, string subject, string keywords, bool stripAll)
        {
            var outPath = AppPaths.Output(pdf, "pdf", stripAll ? "clean" : "edited");
            using var doc = OpenModify(pdf);
            var i = doc.Info;
            if (stripAll)
            {
                foreach (var key in i.Elements.Keys.ToList()) i.Elements.Remove(key);
                doc.Internals.Catalog.Elements.Remove("/Metadata");
            }
            else
            {
                i.Title = title ?? ""; i.Author = author ?? ""; i.Subject = subject ?? ""; i.Keywords = keywords ?? "";
            }
            doc.Save(outPath);
            return outPath;
        }

        public class PageOp { public int Source; public int AddRotation; }

        /// <summary>Writes the pages in the given order with extra rotation (page editor / extract).</summary>
        public static string RebuildPages(string pdf, IList<PageOp> pages, string suffix)
        {
            if (pages.Count == 0) throw new UserFacingException("There are no pages left to save.");
            var outPath = AppPaths.Output(pdf, "pdf", suffix);
            using var src = OpenImport(pdf);
            using var doc = new PdfDocument();
            doc.Info.Title = src.Info.Title;
            doc.Info.Author = src.Info.Author;
            foreach (var op in pages)
            {
                var p = doc.AddPage(src.Pages[op.Source]);
                if (op.AddRotation % 360 != 0)
                    p.Rotate = (((p.Rotate + op.AddRotation) % 360) + 360) % 360;
            }
            doc.Save(outPath);
            return outPath;
        }
    }
}
