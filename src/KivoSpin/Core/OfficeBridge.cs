using System.Diagnostics;

namespace KivoSpin.Core
{
    /// <summary>Turns Word documents into PDFs using Microsoft Word if installed, otherwise LibreOffice.</summary>
    public static class OfficeBridge
    {
        public static bool Available => WordInstalled || FindLibreOffice() != null;

        private static bool WordInstalled => Type.GetTypeFromProgID("Word.Application") != null;

        public static string ToPdf(string src, string outPdf)
        {
            if (WordInstalled && TryWord(src, outPdf)) return outPdf;

            var soffice = FindLibreOffice();
            if (soffice != null && TryLibreOffice(soffice, src, outPdf)) return outPdf;

            throw new UserFacingException("Word files need Microsoft Word or the free LibreOffice installed to convert.");
        }

        private static bool TryWord(string src, string dst)
        {
            var type = Type.GetTypeFromProgID("Word.Application");
            if (type == null) return false;
            bool wordWasRunning = Process.GetProcessesByName("WINWORD").Length > 0;
            bool ok = false;

            var thread = new Thread(() =>
            {
                dynamic word = null, document = null;
                try
                {
                    word = Activator.CreateInstance(type);
                    if (!wordWasRunning) word.Visible = false;
                    word.DisplayAlerts = 0;
                    // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles)
                    document = word.Documents.Open(src, false, true, false);
                    document.ExportAsFixedFormat(dst, 17); // 17 = wdExportFormatPDF
                    ok = true;
                }
                catch (Exception ex) { Log.Error("Word export failed", ex); }
                finally
                {
                    try { if (document != null) document.Close(false); } catch { }
                    // Never quit a Word the user already had open (it would close their documents).
                    try { if (word != null && !wordWasRunning) word.Quit(false); } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join(TimeSpan.FromMinutes(3));
            return ok && File.Exists(dst);
        }

        public static string FindLibreOffice()
        {
            var candidates = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            foreach (var root in candidates.Where(r => !string.IsNullOrEmpty(r)))
            {
                var p = Path.Combine(root, "LibreOffice", "program", "soffice.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static bool TryLibreOffice(string soffice, string src, string dst)
        {
            try
            {
                var outDir = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(outDir);
                var psi = new ProcessStartInfo(soffice)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                foreach (var a in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", outDir, src })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return false;
                if (!p.WaitForExit(180_000)) { try { p.Kill(); } catch { } return false; }
                var produced = Directory.GetFiles(outDir, "*.pdf").FirstOrDefault();
                if (produced == null) return false;
                File.Move(produced, dst, true);
                try { Directory.Delete(outDir, true); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("LibreOffice export failed", ex);
                return false;
            }
        }
    }
}
