using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    /// <summary>Shows what a file reveals about itself and lets you strip or edit it.</summary>
    public class MetadataWindow : KivoWindow
    {
        public string Output { get; private set; }
        private readonly string _src;

        public MetadataWindow(string src) : base("Metadata  ·  " + Path.GetFileName(src), 620)
        {
            _src = src;
            var root = new StackPanel();
            if (FileKinds.Of(src) == FileKind.Pdf) BuildPdf(root); else BuildImage(root);
            SetBody(root);
        }

        private void BuildImage(StackPanel root)
        {
            var rows = Tools.ReadImageMetadata(_src, out bool gps);
            if (gps)
            {
                var warn = new Border
                {
                    Background = Theme.B(0x30, Theme.Warn), BorderBrush = Theme.B(Theme.Warn), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                    Child = Theme.Text("This photo contains GPS location. Anyone you send it to can see where it was taken.", 12.5, Theme.B(Theme.Warn))
                };
                root.Children.Add(warn);
            }
            root.Children.Add(Table(rows));

            var close = new KButton("Close");
            close.Click += () => Close();
            var strip = new KButton("Save copy without metadata", true, "");
            strip.Click += () => Run(() => Tools.StripMetadata(_src));
            root.Children.Add(Footer(null, new UIElement[] { close, strip }));
        }

        private void BuildPdf(StackPanel root)
        {
            var info = PdfTools.ReadInfo(_src);
            var title = Theme.Input(info.Title);
            var author = Theme.Input(info.Author);
            var subject = Theme.Input(info.Subject);
            var keywords = Theme.Input(info.Keywords);
            root.Children.Add(Field("Title", title));
            root.Children.Add(Field("Author", author));
            root.Children.Add(Field("Subject", subject));
            root.Children.Add(Field("Keywords", keywords));

            var ro = new List<(string, string)>
            {
                ("Pages", info.Pages.ToString()),
                ("Made with", info.Creator ?? ""),
                ("Producer", info.Producer ?? ""),
                ("Created", info.Created ?? ""),
                ("Modified", info.Modified ?? ""),
                ("XMP metadata", info.HasXmp ? "Present" : "None"),
                ("File size", Imaging.HumanSize(new FileInfo(_src).Length))
            };
            var t = Table(ro);
            t.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(t);

            var close = new KButton("Close");
            close.Click += () => Close();
            var strip = new KButton("Remove all", false, "");
            strip.Click += () => Run(() => PdfTools.WriteInfo(_src, null, null, null, null, true));
            var save = new KButton("Save copy", true, "");
            save.Click += () => Run(() => PdfTools.WriteInfo(_src, title.Text, author.Text, subject.Text, keywords.Text, false));
            root.Children.Add(Footer(new UIElement[] { strip }, new UIElement[] { close, save }));
        }

        private static UIElement Field(string label, TextBox box)
        {
            var sp = new StackPanel();
            var l = Theme.Label(label);
            l.Margin = new Thickness(0, 8, 0, 4);
            sp.Children.Add(l);
            sp.Children.Add(box);
            return sp;
        }

        private static FrameworkElement Table(List<(string key, string value)> rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int r = 0;
            foreach (var (k, v) in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var kt = new TextBlock { Text = k, FontFamily = Theme.Body, FontSize = 12, Foreground = Theme.InkDimBrush, Margin = new Thickness(0, 4, 10, 4), TextTrimming = TextTrimming.CharacterEllipsis };
                var vt = new TextBox
                {
                    Text = v ?? "", IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                    Foreground = Theme.InkBrush, FontFamily = Theme.Body, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3)
                };
                Grid.SetRow(kt, r); Grid.SetRow(vt, r); Grid.SetColumn(vt, 1);
                grid.Children.Add(kt); grid.Children.Add(vt);
                r++;
            }
            if (rows.Count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                grid.Children.Add(Theme.Text("No metadata found.", 12.5, Theme.InkDimBrush));
            }
            return new Border
            {
                Background = Theme.WellBrush, BorderBrush = Theme.EdgeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 8, 14, 8),
                Child = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid }
            };
        }

        private void Run(Func<string> work)
        {
            try
            {
                Output = work();
                Close();
            }
            catch (UserFacingException ex) { KDialog.Info("Couldn't save", ex.Message); }
            catch (Exception ex) { Log.Error("Metadata save failed", ex); KDialog.Info("Couldn't save", "Something went wrong saving the file."); }
        }
    }
}
