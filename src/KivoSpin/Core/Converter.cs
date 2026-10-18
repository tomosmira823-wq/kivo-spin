using ImageMagick;

namespace KivoSpin.Core
{
    /// <summary>The "CONVERT" side of the wheel.</summary>
    public static class Converter
    {
        public static List<string> Convert(string src, string target)
        {
            if (!FileKinds.CanConvert(src, target))
                throw new UserFacingException(Path.GetExtension(src).ToUpperInvariant().TrimStart('.') + " can't be turned into " + target.ToUpperInvariant() + ".");

            switch (FileKinds.Of(src))
            {
                case FileKind.Image:
                    if (target == "pdf") return new List<string> { PdfTools.BuildPdf(new[] { src }, AppPaths.Output(src, "pdf")) };
                    if (target == "docx") return new List<string> { ImageToDocx(src) };
                    return new List<string> { ImageToImage(src, target) };

                case FileKind.Pdf:
                    if (target == "docx") return new List<string> { PdfTools.ToDocx(src, AppPaths.Output(src, "docx")) };
                    return PdfTools.ToImages(src, target);

                case FileKind.Word:
                    if (target == "pdf")
                        return new List<string> { OfficeBridge.ToPdf(src, AppPaths.Output(src, "pdf")) };
                    var tmp = AppPaths.NewTempFile("pdf");
                    try
                    {
                        OfficeBridge.ToPdf(src, tmp);
                        // Name the results after the Word file, not the temp PDF.
                        var named = Path.Combine(Path.GetDirectoryName(tmp), Path.GetFileNameWithoutExtension(src) + ".pdf");
                        named = AppPaths.Unique(named);
                        File.Move(tmp, named);
                        tmp = named;
                        var outs = PdfTools.ToImages(tmp, target);
                        // ToImages put results next to the temp file's folder only if it couldn't write the original folder;
                        // move them beside the Word file.
                        return MoveBeside(outs, src);
                    }
                    finally { AppPaths.TryDelete(tmp); }
            }
            throw new UserFacingException("This file type isn't supported yet.");
        }

        private static List<string> MoveBeside(List<string> outputs, string src)
        {
            var dir = AppPaths.OutputDirFor(src);
            var moved = new List<string>();
            foreach (var o in outputs)
            {
                if (string.Equals(Path.GetDirectoryName(o), dir, StringComparison.OrdinalIgnoreCase)) { moved.Add(o); continue; }
                var dest = AppPaths.Unique(Path.Combine(dir, Path.GetFileName(o)));
                if (Directory.Exists(o)) Directory.Move(o, dest); else File.Move(o, dest);
                moved.Add(dest);
            }
            return moved;
        }

        public static string ImageToImage(string src, string target)
        {
            using var img = Imaging.Load(src);
            var outPath = AppPaths.Output(src, target);
            Imaging.SaveAs(img, target, outPath, AppSettings.Current.Quality);
            return outPath;
        }

        public static string ImageToDocx(string src)
        {
            using var img = Imaging.Load(src);
            using var ms = new MemoryStream();
            img.Write(ms, MagickFormat.Png);
            var outPath = AppPaths.Output(src, "docx");
            DocxWriter.Write(outPath, new DocBlock[] { new DocImage { Png = ms.ToArray(), W = (int)img.Width, H = (int)img.Height } });
            return outPath;
        }

        public static string Combine(IList<string> files, Action<int, int> progress)
        {
            var ordered = files.ToList();
            var outPath = AppPaths.Output(ordered[0], "pdf", "combined");
            return PdfTools.BuildPdf(ordered, outPath, progress);
        }
    }
}
