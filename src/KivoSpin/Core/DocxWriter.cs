using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace KivoSpin.Core
{
    public abstract class DocBlock { }
    public class DocText : DocBlock { public string Text; }
    public class DocImage : DocBlock { public byte[] Png; public int W; public int H; }
    public class DocPageBreak : DocBlock { }

    /// <summary>Writes simple Word documents (A4, text paragraphs and pictures).</summary>
    public static class DocxWriter
    {
        // A4 with 2 cm margins, in EMU (1 twip = 635 EMU).
        private const long MaxWidthEmu = (11906L - 2 * 1134L) * 635L;
        private const long MaxHeightEmu = (16838L - 2 * 1134L - 600L) * 635L;

        public static void Write(string path, IEnumerable<DocBlock> blocks)
        {
            using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body;
            uint id = 1;

            foreach (var block in blocks)
            {
                switch (block)
                {
                    case DocText t:
                        body.AppendChild(new Paragraph(new Run(new Text(Clean(t.Text)) { Space = SpaceProcessingModeValues.Preserve })));
                        break;
                    case DocPageBreak _:
                        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                        break;
                    case DocImage im:
                        var part = main.AddImagePart(ImagePartType.Png);
                        using (var ms = new MemoryStream(im.Png)) part.FeedData(ms);
                        string relId = main.GetIdOfPart(part);
                        long cx = im.W * 9525L, cy = im.H * 9525L;
                        double s = Math.Min(1.0, Math.Min((double)MaxWidthEmu / cx, (double)MaxHeightEmu / cy));
                        cx = (long)(cx * s); cy = (long)(cy * s);
                        body.AppendChild(new Paragraph(new Run(Picture(relId, cx, cy, id++))));
                        break;
                }
            }

            body.AppendChild(new SectionProperties(
                new PageSize { Width = 11906U, Height = 16838U },
                new PageMargin { Top = 1134, Bottom = 1134, Left = 1134U, Right = 1134U, Header = 708U, Footer = 708U, Gutter = 0U }));
            main.Document.Save();
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\t' || c >= 0x20 && !char.IsSurrogate(c) && c != '\uFFFE' && c != '\uFFFF') sb.Append(c);
                else if (char.IsSurrogate(c)) sb.Append(c);
            }
            // drop lone surrogates
            var str = sb.ToString();
            var outSb = new System.Text.StringBuilder(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                if (char.IsHighSurrogate(str[i]) && i + 1 < str.Length && char.IsLowSurrogate(str[i + 1])) { outSb.Append(str[i]).Append(str[i + 1]); i++; }
                else if (!char.IsSurrogate(str[i])) outSb.Append(str[i]);
            }
            return outSb.ToString();
        }

        private static Drawing Picture(string relId, long cx, long cy, uint id)
        {
            return new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cx, Cy = cy },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = (UInt32Value)id, Name = "Picture " + id },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = (UInt32Value)0U, Name = "image" + id + ".png" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = (UInt32Value)0U,
                    DistanceFromBottom = (UInt32Value)0U,
                    DistanceFromLeft = (UInt32Value)0U,
                    DistanceFromRight = (UInt32Value)0U
                });
        }
    }
}
