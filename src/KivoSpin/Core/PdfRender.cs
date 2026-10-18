using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;

namespace KivoSpin.Core
{
    /// <summary>Renders PDF pages to bitmaps with PDFium (via Docnet). PDFium isn't thread-safe, so every call is serialised.</summary>
    public static class PdfRender
    {
        private static readonly object Gate = new object();
        private const int MaxSidePx = 9000;

        public static int PageCount(string pdf)
        {
            lock (Gate)
            {
                try
                {
                    using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
                    return reader.GetPageCount();
                }
                catch (Exception ex) { throw Friendly(ex); }
            }
        }

        /// <summary>Page sizes in PDF points, as displayed (rotation applied).</summary>
        public static List<(double w, double h)> PageSizes(string pdf)
        {
            lock (Gate)
            {
                try
                {
                    var list = new List<(double, double)>();
                    using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
                    int n = reader.GetPageCount();
                    for (int i = 0; i < n; i++)
                    {
                        using var pr = reader.GetPageReader(i);
                        list.Add((pr.GetPageWidth(), pr.GetPageHeight()));
                    }
                    return list;
                }
                catch (Exception ex) { throw Friendly(ex); }
            }
        }

        public static BitmapSource RenderPage(string pdf, int index, double dpi)
        {
            BitmapSource result = null;
            RenderPages(pdf, dpi, new[] { index }, (i, bmp, eff) => result = bmp);
            return result;
        }

        /// <summary>
        /// Renders the requested pages (null = all). Callback gets (pageIndex, bitmap on white, effectiveDpi).
        /// Very large pages are rendered at a lower DPI so they don't run out of memory.
        /// </summary>
        public static void RenderPages(string pdf, double dpi, IEnumerable<int> pages, Action<int, BitmapSource, double> each, Func<bool> cancelled = null)
        {
            lock (Gate)
            {
                try
                {
                    List<int> wanted;
                    double maxPt = 1;
                    using (var probe = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0)))
                    {
                        int n = probe.GetPageCount();
                        wanted = (pages ?? Enumerable.Range(0, n)).Where(i => i >= 0 && i < n).ToList();
                        foreach (var i in wanted)
                        {
                            using var pr = probe.GetPageReader(i);
                            maxPt = Math.Max(maxPt, Math.Max(pr.GetPageWidth(), pr.GetPageHeight()));
                        }
                    }

                    double scale = Math.Min(dpi / 72.0, MaxSidePx / maxPt);
                    double effDpi = scale * 72.0;

                    using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(scale));
                    foreach (var i in wanted)
                    {
                        if (cancelled != null && cancelled()) return;
                        using var pr = reader.GetPageReader(i);
                        int w = pr.GetPageWidth(), h = pr.GetPageHeight();
                        var raw = pr.GetImage();
                        each(i, ToBitmapOnWhite(raw, w, h), effDpi);
                    }
                }
                catch (UserFacingException) { throw; }
                catch (Exception ex) { throw Friendly(ex); }
            }
        }

        public static string PageText(string pdf, int index)
        {
            lock (Gate)
            {
                try
                {
                    using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
                    using var pr = reader.GetPageReader(index);
                    return pr.GetText() ?? "";
                }
                catch { return ""; }
            }
        }

        // PDFium leaves the page background transparent; composite it onto white.
        private static BitmapSource ToBitmapOnWhite(byte[] bgra, int w, int h)
        {
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                if (a == 255) continue;
                int ia = 255 - a;
                bgra[i] = (byte)((bgra[i] * a + 255 * ia) / 255);
                bgra[i + 1] = (byte)((bgra[i + 1] * a + 255 * ia) / 255);
                bgra[i + 2] = (byte)((bgra[i + 2] * a + 255 * ia) / 255);
                bgra[i + 3] = 255;
            }
            var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bs.Freeze();
            return bs;
        }

        public static Exception Friendly(Exception ex)
        {
            Log.Error("PDF error", ex);
            var m = ex.Message ?? "";
            if (m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                return new UserFacingException("This PDF is password-protected. Open it, remove the password, then try again.");
            return new UserFacingException("Couldn't read this PDF. It may be damaged or protected.");
        }
    }
}
