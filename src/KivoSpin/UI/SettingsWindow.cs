using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    public class SettingsWindow : KivoWindow
    {
        private static SettingsWindow _open;

        public static void ShowSingle()
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new SettingsWindow();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
            _open.Activate();
        }

        private readonly TextBlock _folderText;
        private readonly TextBlock _updateText;

        private SettingsWindow() : base("Settings", 560)
        {
            var s = AppSettings.Current;
            var root = new StackPanel();

            // ----- wheel
            var wheel = new StackPanel();
            wheel.Children.Add(Row("Trigger key", "Hold this while dragging a file.",
                Seg(new[] { "Shift", "Ctrl", "Alt" }, (int)s.Trigger, i => s.Trigger = (TriggerKey)i)));
            wheel.Children.Add(Row("Wheel size", null,
                Seg(new[] { "Small", "Medium", "Large" }, (int)s.Size, i => s.Size = (WheelSize)i)));
            wheel.Children.Add(Row("Pause Kivo Spin", "Stops the wheel appearing until you turn it back on.",
                Toggle(s.Paused, v => s.Paused = v)));
            root.Children.Add(Section("Wheel", wheel));

            // ----- output
            var output = new StackPanel();
            _folderText = Theme.Text(FolderLabel(), 12, Theme.InkDimBrush, false);
            var where = Seg(new[] { "Next to original", "One folder" }, s.SaveNextToOriginal ? 0 : 1, i =>
            {
                if (i == 1 && (string.IsNullOrWhiteSpace(s.OutputFolder) || !Directory.Exists(s.OutputFolder)))
                {
                    if (!PickFolder()) { s.SaveNextToOriginal = true; return; }
                }
                s.SaveNextToOriginal = i == 0;
                _folderText.Text = FolderLabel();
            });
            output.Children.Add(Row("Save results", null, where));
            var folderRow = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
            var choose = new KButton("Choose…") { Padding = new Thickness(10, 5, 10, 5) };
            choose.Click += () => { if (PickFolder()) { s.SaveNextToOriginal = false; ((KSegment)where).Selected = 1; _folderText.Text = FolderLabel(); } };
            DockPanel.SetDock(choose, Dock.Right);
            folderRow.Children.Add(choose);
            _folderText.VerticalAlignment = VerticalAlignment.Center;
            folderRow.Children.Add(_folderText);
            output.Children.Add(folderRow);
            output.Children.Add(Row("Open the folder when done", null, Toggle(s.OpenFolderWhenDone, v => s.OpenFolderWhenDone = v)));
            root.Children.Add(Section("Output", output));

            // ----- quality
            var q = new StackPanel();
            q.Children.Add(Row("Convert quality", "JPG, WEBP, HEIC and AVIF.", Slider(50, 100, s.Quality, v => s.Quality = (int)v)));
            q.Children.Add(Row("Compress quality", "Lower = smaller files.", Slider(30, 95, s.CompressQuality, v => s.CompressQuality = (int)v)));
            var maxSides = new[] { 0, 3840, 2560, 1920, 1280 };
            int msIdx = Math.Max(0, Array.IndexOf(maxSides, s.CompressMaxSide));
            q.Children.Add(Row("Compress: max size", "Shrinks big photos when compressing.",
                Seg(new[] { "Off", "4K", "2560", "1920", "1280" }, msIdx, i => s.CompressMaxSide = maxSides[i])));
            var dpis = new[] { 150, 200, 300 };
            int dpiIdx = Math.Max(0, Array.IndexOf(dpis, s.PdfDpi));
            q.Children.Add(Row("PDF → image sharpness", "Dots per inch.", Seg(new[] { "150", "200", "300" }, dpiIdx, i => s.PdfDpi = dpis[i])));
            root.Children.Add(Section("Quality", q));

            // ----- app
            var app = new StackPanel();
            app.Children.Add(Row("Start with Windows", null, Toggle(s.StartWithWindows, v => { s.StartWithWindows = v; StartupManager.Sync(); })));
            app.Children.Add(Row("Install updates automatically", "Otherwise Kivo Spin asks before restarting.", Toggle(s.AutoInstallUpdates, v => s.AutoInstallUpdates = v)));
            _updateText = Theme.Text("Version " + Updater.CurrentVersion, 12, Theme.InkDimBrush, false);
            _updateText.VerticalAlignment = VerticalAlignment.Center;
            var upRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var check = new KButton("Check for updates", false, "") { Padding = new Thickness(10, 5, 10, 5) };
            check.Click += async () =>
            {
                _updateText.Text = "Checking…";
                await Updater.CheckAsync(manual: true);
                _updateText.Text = "Version " + Updater.CurrentVersion;
            };
            DockPanel.SetDock(check, Dock.Right);
            upRow.Children.Add(check);
            upRow.Children.Add(_updateText);
            app.Children.Add(upRow);
            root.Children.Add(Section("App", app));

            var how = new KButton("How it works", false, "");
            how.Click += () => new WelcomeWindow().Show();
            var logs = new KButton("Logs", false, "");
            logs.Click += () => { if (File.Exists(Log.FilePath)) Ui.ShowInFolder(Log.FilePath); };
            var quit = new KButton("Quit Kivo Spin", false, "");
            quit.Click += () => { Close(); App.Instance?.Quit(); };
            var done = new KButton("Done", true);
            done.Click += () => Close();
            root.Children.Add(Footer(new UIElement[] { how, logs }, new UIElement[] { quit, done }));

            SetBody(new ScrollViewer { Content = root, MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
            Closed += (s2, e) => AppSettings.Save();
        }

        private static string FolderLabel()
        {
            var s = AppSettings.Current;
            if (s.SaveNextToOriginal) return "Results are saved beside the original file.";
            return string.IsNullOrWhiteSpace(s.OutputFolder) ? "No folder chosen" : s.OutputFolder;
        }

        private bool PickFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Where should Kivo Spin save results?" };
            if (!string.IsNullOrEmpty(AppSettings.Current.OutputFolder) && Directory.Exists(AppSettings.Current.OutputFolder))
                dlg.InitialDirectory = AppSettings.Current.OutputFolder;
            if (dlg.ShowDialog(this) == true)
            {
                AppSettings.Current.OutputFolder = dlg.FolderName;
                AppSettings.Save();
                return true;
            }
            return false;
        }

        private static UIElement Section(string title, UIElement body)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            var l = Theme.Label(title);
            l.Foreground = Theme.AccentBrush;
            l.Margin = new Thickness(2, 10, 0, 6);
            sp.Children.Add(l);
            sp.Children.Add(Theme.Card(body));
            return sp;
        }

        private static UIElement Row(string title, string hint, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new TextBlock { Text = title, FontFamily = Theme.Body, FontSize = 13.5, Foreground = Theme.InkBrush });
            if (!string.IsNullOrEmpty(hint)) sp.Children.Add(Theme.Text(hint, 11.5, Theme.InkFaintBrush));
            g.Children.Add(sp);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        private static FrameworkElement Seg(string[] options, int selected, Action<int> changed)
        {
            var seg = new KSegment(options, selected);
            seg.Changed += i => { changed(i); AppSettings.Save(); };
            return seg;
        }

        private static FrameworkElement Toggle(bool on, Action<bool> changed)
        {
            var t = new KToggle(on);
            t.Toggled += v => { changed(v); AppSettings.Save(); };
            return t;
        }

        private static FrameworkElement Slider(double min, double max, double value, Action<double> changed)
        {
            var sl = new KSlider(min, max, value, 210);
            sl.ValueChanged += v => { changed(v); AppSettings.Save(); };
            return sl;
        }
    }
}
