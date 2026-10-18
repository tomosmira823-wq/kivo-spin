using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    /// <summary>Pick a colour (or an image) to put behind a cut-out.</summary>
    public class BackgroundWindow : KivoWindow
    {
        public string ColorHex { get; private set; }
        public string ImagePath { get; private set; }
        public bool Ok { get; private set; }

        private readonly Border _preview;
        private readonly TextBox _hex;
        private readonly TextBlock _imgLabel;

        private static readonly string[] Swatches =
        {
            "#FFFFFF", "#F4F1EA", "#E5E7EB", "#9CA3AF", "#111111", "#3CF0B4", "#5AC8FA", "#0A84FF",
            "#FF8FB1", "#FF9F43", "#FFD60A", "#B28DFF"
        };

        public BackgroundWindow(BitmapSource thumb, int fileCount) : base("Add background", 520)
        {
            var root = new StackPanel();
            if (fileCount > 1) root.Children.Add(Theme.Text($"Applies to all {fileCount} images. Images without a transparent background get cut out first.", 12.5, Theme.InkDimBrush));
            else root.Children.Add(Theme.Text("If the image has no transparent background, Kivo Spin cuts the subject out first.", 12.5, Theme.InkDimBrush));

            var start = AppSettings.Current.LastBgColor ?? "#FFFFFF";
            _preview = new Border
            {
                Height = 220, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 14, 0, 0),
                BorderBrush = Theme.EdgeBrush, BorderThickness = new Thickness(1),
                Child = new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Thickness(12) }
            };
            root.Children.Add(_preview);

            root.Children.Add(Theme.Label("Colour"));
            var wrap = new WrapPanel();
            foreach (var hex in Swatches)
            {
                var h = hex;
                var b = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 8),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                    BorderBrush = Theme.B(0x55, 0x5A, 0x62), BorderThickness = new Thickness(2), Cursor = Cursors.Hand
                };
                b.MouseLeftButtonUp += (s, e) => { _hex.Text = h; };
                wrap.Children.Add(b);
            }
            root.Children.Add(wrap);

            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _hex = Theme.Input(start, 120);
            _hex.TextChanged += (s, e) => { ImagePath = null; UpdatePreview(); };
            hexRow.Children.Add(_hex);
            var pick = new KButton("Use an image…", false, "") { Margin = new Thickness(12, 0, 0, 0) };
            pick.Click += () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a background image",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.heic;*.avif;*.bmp;*.tif;*.tiff|All files|*.*"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    ImagePath = dlg.FileName;
                    _imgLabel.Text = Path.GetFileName(ImagePath);
                    UpdatePreview();
                }
            };
            hexRow.Children.Add(pick);
            root.Children.Add(hexRow);
            _imgLabel = Theme.Text("", 12, Theme.InkDimBrush);
            _imgLabel.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_imgLabel);

            var cancel = new KButton("Cancel");
            cancel.Click += () => Close();
            var ok = new KButton("Apply", true, "");
            ok.Click += () =>
            {
                ColorHex = NormaliseHex(_hex.Text) ?? "#FFFFFF";
                AppSettings.Current.LastBgColor = ColorHex;
                AppSettings.Save();
                Ok = true;
                Close();
            };
            root.Children.Add(Footer(null, new UIElement[] { cancel, ok }));
            SetBody(root);
            UpdatePreview();
        }

        private static string NormaliseHex(string s)
        {
            s = (s ?? "").Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (s.Length == 4) s = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
            if (s.Length != 7) return null;
            return s.Skip(1).All(Uri.IsHexDigit) ? s.ToUpperInvariant() : null;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            if (!string.IsNullOrEmpty(ImagePath))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(ImagePath); bi.DecodePixelWidth = 600; bi.EndInit(); bi.Freeze();
                    _preview.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                    return;
                }
                catch { }
            }
            var hex = NormaliseHex(_hex?.Text);
            if (hex != null) _preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
