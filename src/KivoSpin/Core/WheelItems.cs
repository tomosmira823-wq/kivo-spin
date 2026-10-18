namespace KivoSpin.Core
{
    public class WheelItem
    {
        public string Id;       // "to:png", "compress", ...
        public string Label;    // shown on the wedge
        public string Glyph;    // Segoe Fluent/MDL2 icon (tools page)
        public bool Enabled = true;
        public string Verb;     // used in the caption, e.g. "→ PNG"
    }

    public static class WheelPages
    {
        private static readonly (string id, string label)[] ConvertTargets =
        {
            ("png", "PNG"), ("jpg", "JPG"), ("webp", "WEBP"), ("heic", "HEIC"), ("avif", "AVIF"),
            ("tiff", "TIFF"), ("bmp", "BMP"), ("ico", "ICO"), ("pdf", "PDF"), ("docx", "DOCX")
        };

        public static List<WheelItem> Convert(IReadOnlyList<string> files)
        {
            var list = new List<WheelItem>();
            foreach (var (id, label) in ConvertTargets)
            {
                bool enabled = files.Any(f => FileKinds.CanConvert(f, id) && FileKinds.FormatId(f) != id);
                list.Add(new WheelItem { Id = "to:" + id, Label = label, Enabled = enabled, Verb = "→ " + label });
            }
            return list;
        }

        public static List<WheelItem> Tools(IReadOnlyList<string> files)
        {
            bool anyImage = files.Any(f => FileKinds.Of(f) == FileKind.Image);
            bool anyPdf = files.Any(f => FileKinds.Of(f) == FileKind.Pdf);
            bool imgOrPdf = anyImage || anyPdf;
            bool many = files.Count >= 2;
            bool allCombinable = files.All(f => FileKinds.Of(f) != FileKind.Unknown);

            var list = new List<WheelItem>
            {
                new WheelItem { Id = "compress", Label = "COMPRESS", Glyph = "", Enabled = imgOrPdf, Verb = "Compress" },
                new WheelItem { Id = "metadata", Label = "METADATA", Glyph = "", Enabled = imgOrPdf, Verb = many ? "Strip metadata" : "View / strip metadata" },
                new WheelItem { Id = "edit", Label = "EDIT", Glyph = "", Enabled = imgOrPdf, Verb = anyPdf && !anyImage ? "Edit pages" : "Edit" },
                new WheelItem { Id = "annotate", Label = "ANNOTATE", Glyph = "", Enabled = imgOrPdf, Verb = "Annotate" },
                new WheelItem { Id = "removebg", Label = "REMOVE BG", Glyph = "", Enabled = anyImage, Verb = "Remove background" },
                new WheelItem { Id = "addbg", Label = "ADD BG", Glyph = "", Enabled = anyImage, Verb = "Add background" },
                new WheelItem { Id = "crop", Label = "CROP", Glyph = "", Enabled = imgOrPdf, Verb = "Crop" },
                new WheelItem { Id = "redact", Label = "REDACT", Glyph = "", Enabled = imgOrPdf, Verb = "Redact" },
            };
            if (many)
                list.Add(new WheelItem { Id = "combine", Label = "COMBINE", Glyph = "", Enabled = allCombinable, Verb = "Combine into one PDF" });
            return list;
        }
    }
}
