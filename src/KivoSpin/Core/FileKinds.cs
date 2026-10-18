namespace KivoSpin.Core
{
    public enum FileKind { Unknown, Image, Pdf, Word }

    public static class FileKinds
    {
        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".jfif", ".jpe", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff",
            ".bmp", ".gif", ".ico", ".jxl", ".psd", ".tga", ".svg", ".dds", ".jp2", ".exr", ".hdr", ".qoi"
        };

        private static readonly HashSet<string> WordExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".docx", ".doc", ".odt", ".rtf", ".docm", ".dotx"
        };

        public static FileKind Of(string path)
        {
            var e = Path.GetExtension(path);
            if (string.IsNullOrEmpty(e)) return FileKind.Unknown;
            if (ImageExts.Contains(e)) return FileKind.Image;
            if (e.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return FileKind.Pdf;
            if (WordExts.Contains(e)) return FileKind.Word;
            return FileKind.Unknown;
        }

        /// <summary>Normalised format id that matches the wheel ids (png, jpg, webp, heic, avif, tiff, bmp, ico, pdf, docx…).</summary>
        public static string FormatId(string path)
        {
            var e = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            switch (e)
            {
                case "jpeg": case "jfif": case "jpe": return "jpg";
                case "tif": return "tiff";
                case "heif": return "heic";
                case "doc": case "odt": case "rtf": case "docm": case "dotx": return "docx";
                default: return e;
            }
        }

        public static readonly string[] ImageTargets = { "png", "jpg", "webp", "heic", "avif", "tiff", "bmp", "ico" };

        public static bool CanConvert(string path, string target)
        {
            var kind = Of(path);
            bool imgTarget = ImageTargets.Contains(target);
            switch (kind)
            {
                case FileKind.Image: return imgTarget || target == "pdf" || target == "docx";
                case FileKind.Pdf: return imgTarget || target == "docx";
                case FileKind.Word: return imgTarget || target == "pdf";
                default: return false;
            }
        }
    }
}
