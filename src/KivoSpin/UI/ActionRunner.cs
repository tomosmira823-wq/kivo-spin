using System.Windows;
using System.Windows.Media.Imaging;
using ImageMagick;
using KivoSpin.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace KivoSpin.UI
{
    /// <summary>Runs whatever was dropped on the wheel and reports back with a toast.</summary>
    public static class ActionRunner
    {
        public static bool Busy { get; private set; }

        public static void Run(string id, List<string> files)
        {
            if (files == null || files.Count == 0) return;
            if (Busy)
            {
                ToastWindow.Show("One moment", "Kivo Spin is still working on the last drop.");
                return;
            }
            Log.Info($"Action {id} on {files.Count} file(s)");
            try
            {
                switch (id)
                {
                    case "crop": RunInteractive(files, CropOne, "cropped"); return;
                    case "edit": RunInteractive(files, EditOne, "edited"); return;
                    case "annotate": RunInteractive(files, f => MarkupOne(f, false), "annotated"); return;
                    case "redact": RunInteractive(files, f => MarkupOne(f, true), "redacted"); return;
                    case "addbg": AddBackground(files); return;
                    case "metadata":
                        if (files.Count == 1) { RunInteractive(files, MetadataOne, "saved"); return; }
                        RunBatch("Removing metadata", files, (f, status) => new List<string> { Tools.StripMetadata(f) });
                        return;
                    case "compress": RunCompress(files); return;
                    case "removebg":
                        RunBatch("Removing background", files.Where(f => FileKinds.Of(f) == FileKind.Image).ToList(),
                            (f, status) => new List<string> { BackgroundRemover.Remove(f, status) });
                        return;
                    case "combine": RunCombine(files); return;
                }
                if (id.StartsWith("to:"))
                {
                    var target = id.Substring(3);
                    RunBatch("Converting to " + target.ToUpperInvariant(), files.Where(f => FileKinds.CanConvert(f, target) && FileKinds.FormatId(f) != target).ToList(),
                        (f, status) => Converter.Convert(f, target));
                }
            }
            catch (Exception ex)
            {
                Busy = false;
                Log.Error("Action failed", ex);
                ToastWindow.Show("Something went wrong", ex is UserFacingException ? ex.Message : "That didn't work. Details are in the log.");
            }
        }

        // ------------------------------------------------------------ batch (background thread)

        private static async void RunBatch(string verb, List<string> files, Func<string, Action<string>, List<string>> work)
        {
            if (files.Count == 0) { ToastWindow.Show("Nothing to do", "None of those files can do that."); return; }
            Busy = true;
            var toast = ToastWindow.Progress(verb, files.Count == 1 ? Path.GetFileName(files[0]) : $"0 of {files.Count}");
            var outputs = new List<string>();
            var errors = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        var f = files[i];
                        int n = i;
                        Ui.Post(() => toast.Update(null, files.Count == 1 ? Path.GetFileName(f) : $"{n + 1} of {files.Count} · {Path.GetFileName(f)}", (double)n / files.Count));
                        try
                        {
                            var res = work(f, msg => Ui.Post(() => toast.Update(null, msg, double.NaN)));
                            if (res != null) outputs.AddRange(res.Where(r => !string.IsNullOrEmpty(r)));
                        }
                        catch (UserFacingException ex) { errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
                        catch (Exception ex)
                        {
                            Log.Error("Failed on " + f, ex);
                            errors.Add(Path.GetFileName(f) + ": " + Friendly(ex));
                        }
                    }
                });
            }
            finally { Busy = false; }
            Finish(toast, outputs, errors, null);
        }

        private static string Friendly(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return "Kivo Spin isn't allowed to save there.";
            if (ex is IOException io && io.Message.Contains("being used")) return "The file is open in another app.";
            if (ex is MagickException) return "The image couldn't be processed.";
            return "That didn't work (details in the log).";
        }

        private static void Finish(ToastWindow toast, List<string> outputs, List<string> errors, string note)
        {
            if (outputs.Count == 0)
            {
                string msg = errors.Count > 0 ? errors[0] + (errors.Count > 1 ? $" (+{errors.Count - 1} more)" : "") : (note ?? "Nothing was changed.");
                toast.Set(errors.Count > 0 ? "Couldn't do that" : "All done", msg, null, errors.Count > 0, errors.Count > 0
                    ? new[] { new ToastAction("Open log", () => Ui.ShowInFolder(Log.FilePath)) }
                    : null);
                return;
            }

            string title = outputs.Count == 1 ? "Saved " + Path.GetFileName(outputs[0]) : $"Saved {outputs.Count} files";
            string message = note ?? (outputs.Count == 1 ? Path.GetDirectoryName(outputs[0]) : "In " + Path.GetDirectoryName(outputs[0]));
            if (errors.Count > 0) message += $"\n{errors.Count} couldn't be done: {errors[0]}";

            var actions = new List<ToastAction> { new ToastAction("Show in folder", () => Ui.ShowInFolder(outputs[0]), true) };
            if (outputs.Count == 1 && File.Exists(outputs[0])) actions.Add(new ToastAction("Open", () => Ui.Open(outputs[0])));
            toast.Set(title, message, null, false, actions.ToArray());

            if (AppSettings.Current.OpenFolderWhenDone) Ui.ShowInFolder(outputs[0]);
        }

        // ------------------------------------------------------------ compress / combine

        private static async void RunCompress(List<string> files)
        {
            files = files.Where(f => FileKinds.Of(f) == FileKind.Image || FileKinds.Of(f) == FileKind.Pdf).ToList();
            if (files.Count == 0) return;
            Busy = true;
            var toast = ToastWindow.Progress("Compressing", files.Count == 1 ? Path.GetFileName(files[0]) : $"0 of {files.Count}");
            var outputs = new List<string>();
            var errors = new List<string>();
            var notes = new List<string>();
            long before = 0, after = 0;
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        var f = files[i]; int n = i;
                        Ui.Post(() => toast.Update(null, files.Count == 1 ? Path.GetFileName(f) : $"{n + 1} of {files.Count} · {Path.GetFileName(f)}", (double)n / files.Count));
                        try
                        {
                            var r = Tools.Compress(f,
                                (t, m) => Ui.Invoke(() => KDialog.Confirm(t, m, "Flatten & shrink", "Keep as is")),
                                (a, b) => Ui.Post(() => toast.Update(null, $"Page {a} of {b}", (double)a / b)));
                            if (r.Outputs.Count > 0)
                            {
                                before += new FileInfo(f).Length;
                                after += r.Outputs.Sum(o => new FileInfo(o).Length);
                                outputs.AddRange(r.Outputs);
                            }
                            if (!string.IsNullOrEmpty(r.Note)) notes.Add(r.Note);
                        }
                        catch (UserFacingException ex) { errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
                        catch (Exception ex) { Log.Error("Compress failed " + f, ex); errors.Add(Path.GetFileName(f) + ": " + Friendly(ex)); }
                    }
                });
            }
            finally { Busy = false; }

            string note = outputs.Count > 0
                ? $"{Imaging.HumanSize(before)} → {Imaging.HumanSize(after)}  ({Math.Round(100 - after * 100.0 / Math.Max(1, before))}% smaller)"
                : notes.FirstOrDefault();
            Finish(toast, outputs, errors, note);
        }

        private static async void RunCombine(List<string> files)
        {
            Busy = true;
            var toast = ToastWindow.Progress("Combining", $"{files.Count} files into one PDF");
            var outputs = new List<string>();
            var errors = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var ordered = files.OrderBy(f => f, new NaturalComparer()).ToList();
                        outputs.Add(Converter.Combine(ordered, (a, b) => Ui.Post(() => toast.Update(null, $"Adding {a} of {b}", (double)a / b))));
                    }
                    catch (UserFacingException ex) { errors.Add(ex.Message); }
                    catch (Exception ex) { Log.Error("Combine failed", ex); errors.Add(Friendly(ex)); }
                });
            }
            finally { Busy = false; }
            Finish(toast, outputs, errors, null);
        }

        private class NaturalComparer : IComparer<string>
        {
            [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern int StrCmpLogicalW(string a, string b);
            public int Compare(string x, string y) => StrCmpLogicalW(Path.GetFileName(x) ?? "", Path.GetFileName(y) ?? "");
        }

        // ------------------------------------------------------------ interactive (UI thread)

        private static async void RunInteractive(List<string> files, Func<string, string> one, string verb)
        {
            files = files.Where(f => FileKinds.Of(f) == FileKind.Image || FileKinds.Of(f) == FileKind.Pdf).ToList();
            var outputs = new List<string>();
            var errors = new List<string>();
            Busy = true;
            try
            {
                foreach (var f in files)
                {
                    try
                    {
                        var o = one(f);
                        if (!string.IsNullOrEmpty(o)) outputs.Add(o);
                    }
                    catch (UserFacingException ex) { errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
                    catch (Exception ex) { Log.Error(verb + " failed " + f, ex); errors.Add(Path.GetFileName(f) + ": " + Friendly(ex)); }
                    await Task.Yield();
                }
            }
            finally { Busy = false; }
            if (outputs.Count == 0 && errors.Count == 0) return; // cancelled
            var toast = ToastWindow.Show("", "");
            Finish(toast, outputs, errors, null);
        }

        /// <summary>Runs heavy work off the UI thread while keeping a modal-ish progress toast.</summary>
        private static T WithProgress<T>(string title, string message, Func<T> work)
        {
            var toast = ToastWindow.Progress(title, message);
            toast.Update(null, null, double.NaN);
            var frame = new System.Windows.Threading.DispatcherFrame();
            T result = default;
            Exception error = null;
            Task.Run(() =>
            {
                try { result = work(); }
                catch (Exception ex) { error = ex; }
                finally { frame.Continue = false; }
            });
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            toast.Dismiss();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        private static string CropOne(string f)
        {
            if (FileKinds.Of(f) == FileKind.Pdf)
            {
                var page = PdfRender.RenderPage(f, 0, 110);
                var w = new CropWindow(page, page.PixelWidth, page.PixelHeight, Path.GetFileName(f) + " (all pages)");
                w.ShowDialog();
                if (w.Result == null) return null;
                var r = w.Result.Value;
                double W = page.PixelWidth, H = page.PixelHeight;
                return WithProgress("Cropping", Path.GetFileName(f), () => PdfTools.Crop(f, r.X / W, r.Y / H, (r.X + r.Width) / W, (r.Y + r.Height) / H));
            }
            else
            {
                int fw, fh; BitmapSource preview;
                using (var img = Imaging.Load(f))
                {
                    fw = (int)img.Width; fh = (int)img.Height;
                    Imaging.FitInside(img, 1400);
                    preview = Imaging.ToBitmapSource(img);
                }
                var w = new CropWindow(preview, fw, fh, Path.GetFileName(f));
                w.ShowDialog();
                if (w.Result == null) return null;
                var r = w.Result.Value;
                if (r.X == 0 && r.Y == 0 && r.Width >= fw && r.Height >= fh) return null;
                return WithProgress("Cropping", Path.GetFileName(f), () => Tools.CropImage(f, r.X, r.Y, r.Width, r.Height));
            }
        }

        private static string EditOne(string f)
        {
            if (FileKinds.Of(f) == FileKind.Pdf)
            {
                int count = PdfRender.PageCount(f);
                var pw = new PdfPagesWindow(f, count);
                pw.ShowDialog();
                if (pw.Result == null) return null;
                var ops = pw.Result;
                bool extract = pw.ExtractOnly;
                return WithProgress("Saving pages", Path.GetFileName(f), () => PdfTools.RebuildPages(f, ops, extract ? "pages" : "edited"));
            }

            int fw, fh;
            MagickImage preview;
            using (var img = Imaging.Load(f)) { fw = (int)img.Width; fh = (int)img.Height; }
            preview = Imaging.LoadPreview(f, 1000);
            var w = new EditWindow(preview, fw, fh, Path.GetFileName(f));
            w.ShowDialog();
            if (w.Result == null || w.Result.IsIdentity) return null;
            var spec = w.Result;
            return WithProgress("Saving", Path.GetFileName(f), () => Tools.SaveEdited(f, spec));
        }

        private static string MetadataOne(string f)
        {
            var w = new MetadataWindow(f);
            w.ShowDialog();
            return w.Output;
        }

        // ------------------------------------------------------------ annotate / redact

        private const double PdfViewDpi = 144;

        private static string MarkupOne(string f, bool redact)
        {
            bool pdf = FileKinds.Of(f) == FileKind.Pdf;
            BitmapSource full = null;
            int pages = 1;
            var cache = new Dictionary<int, BitmapSource>();

            if (pdf) pages = PdfRender.PageCount(f);
            else
            {
                using var img = Imaging.Load(f);
                Imaging.FitInside(img, 6000);
                full = Imaging.ToBitmapSource(img);
            }

            Func<int, BitmapSource> loader = i =>
            {
                if (!pdf) return full;
                if (!cache.TryGetValue(i, out var bmp)) { bmp = PdfRender.RenderPage(f, i, PdfViewDpi); cache[i] = bmp; }
                return bmp;
            };

            var w = new MarkupWindow(redact, Path.GetFileName(f), pages, loader);
            w.ShowDialog();
            if (!w.Saved) return null;
            var marks = w.Pages.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
            string suffix = redact ? "redacted" : "annotated";

            if (!pdf)
            {
                // Render on the UI thread (WPF), save off it.
                var flat = Flatten(full, marks[0], 1.0);
                return WithProgress("Saving", Path.GetFileName(f), () =>
                {
                    using var img = Imaging.FromBitmapSource(flat);
                    if (redact) img.Strip(); // the EXIF thumbnail could still show what was hidden
                    return Imaging.SaveSameFormat(img, f, suffix);
                });
            }

            return SaveMarkedPdf(f, marks, redact, suffix);
        }

        private static BitmapSource Flatten(BitmapSource baseImg, List<Annot> annots, double annotScale)
        {
            int w = baseImg.PixelWidth, h = baseImg.PixelHeight;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(baseImg, new Rect(0, 0, w, h));
                if (annots != null) AnnotRenderer.Draw(dc, annots, annotScale);
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static BitmapSource Overlay(int w, int h, List<Annot> annots, double annotScale)
        {
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen()) AnnotRenderer.Draw(dc, annots, annotScale);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static string SaveMarkedPdf(string f, Dictionary<int, List<Annot>> marks, bool redact, string suffix)
        {
            const double flatDpi = 200;
            var outPath = AppPaths.Output(f, "pdf", suffix);
            var keep = new List<IDisposable>();
            try
            {
                using var src = PdfTools.OpenImport(f);
                using var doc = new PdfDocument();
                doc.Info.Title = src.Info.Title;
                doc.Info.Author = src.Info.Author;
                var sizes = PdfRender.PageSizes(f);

                for (int i = 0; i < src.PageCount; i++)
                {
                    var srcPage = src.Pages[i];
                    if (!marks.TryGetValue(i, out var annots))
                    {
                        doc.AddPage(srcPage);
                        continue;
                    }

                    var mb = srcPage.MediaBox;
                    var cb = srcPage.CropBox;
                    bool plain = srcPage.Rotate % 360 == 0 && (cb.IsEmpty || (Math.Abs(cb.Width - mb.Width) < 0.5 && Math.Abs(cb.Height - mb.Height) < 0.5))
                                 && Math.Abs(mb.X1) < 0.5 && Math.Abs(mb.Y1) < 0.5;

                    if (redact || !plain)
                    {
                        // Flatten: render the page, burn the marks in, replace the page with that image.
                        var bmp = PdfRender.RenderPage(f, i, flatDpi);
                        var flat = Flatten(bmp, annots, flatDpi / PdfViewDpi);
                        var (wPt, hPt) = sizes[i];
                        PdfTools.AddBitmapPage(doc, flat, wPt, hPt, keep, jpeg: !redact, quality: 90);
                    }
                    else
                    {
                        // Keep the real page (text stays selectable) and stamp the marks on top.
                        var page = doc.AddPage(srcPage);
                        double wPt = page.Width.Point, hPt = page.Height.Point;
                        double overlayScale = 2.0; // render overlay at 2× the view resolution for crisp lines
                        int ow = (int)Math.Ceiling(wPt / 72.0 * PdfViewDpi * overlayScale);
                        int oh = (int)Math.Ceiling(hPt / 72.0 * PdfViewDpi * overlayScale);
                        var ov = Overlay(ow, oh, annots, overlayScale);
                        XImage ximg;
                        using (var ovImg = Imaging.FromBitmapSource(ov))
                            ximg = PdfTools.MakeXImage(ovImg, keep, keepAlpha: true);
                        using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                        g.DrawImage(ximg, 0, 0, wPt, hPt);
                    }
                }
                doc.Save(outPath);
                return outPath;
            }
            finally { foreach (var k in keep) { try { k.Dispose(); } catch { } } }
        }

        // ------------------------------------------------------------ backgrounds

        private static void AddBackground(List<string> files)
        {
            files = files.Where(f => FileKinds.Of(f) == FileKind.Image).ToList();
            if (files.Count == 0) return;
            BitmapSource thumb;
            using (var img = Imaging.LoadPreview(files[0], 600)) thumb = Imaging.ToBitmapSource(img);
            var w = new BackgroundWindow(thumb, files.Count);
            w.ShowDialog();
            if (!w.Ok) return;
            var hex = w.ColorHex; var bgImage = w.ImagePath;
            RunBatch("Adding background", files, (f, status) => new List<string> { Tools.AddBackground(f, hex, bgImage, status) });
        }
    }
}
