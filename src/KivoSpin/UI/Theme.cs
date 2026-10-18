using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KivoSpin.UI
{
    /// <summary>Kivo hardware look: dark brushed metal, engraved Michroma lettering, one mint accent.</summary>
    public static class Theme
    {
        public static readonly Color Accent = Color.FromRgb(0x3C, 0xF0, 0xB4);
        public static readonly Color AccentDeep = Color.FromRgb(0x14, 0x9E, 0x74);
        public static readonly Color AccentInk = Color.FromRgb(0x04, 0x1A, 0x12);
        public static readonly Color PanelTop = Color.FromRgb(0x2A, 0x2D, 0x33);
        public static readonly Color PanelBottom = Color.FromRgb(0x15, 0x17, 0x1A);
        public static readonly Color Well = Color.FromRgb(0x0E, 0x10, 0x12);
        public static readonly Color Edge = Color.FromRgb(0x0A, 0x0B, 0x0D);
        public static readonly Color Line = Color.FromRgb(0x33, 0x37, 0x3E);
        public static readonly Color Ink = Color.FromRgb(0xE6, 0xE9, 0xED);
        public static readonly Color InkDim = Color.FromRgb(0x8A, 0x91, 0x9B);
        public static readonly Color InkFaint = Color.FromRgb(0x4C, 0x52, 0x5A);
        public static readonly Color Danger = Color.FromRgb(0xFF, 0x5A, 0x5F);
        public static readonly Color Warn = Color.FromRgb(0xFF, 0xC4, 0x4D);

        public static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush B(byte a, Color c) => B(Color.FromArgb(a, c.R, c.G, c.B));

        public static readonly SolidColorBrush AccentBrush = B(Accent);
        public static readonly SolidColorBrush InkBrush = B(Ink);
        public static readonly SolidColorBrush InkDimBrush = B(InkDim);
        public static readonly SolidColorBrush InkFaintBrush = B(InkFaint);
        public static readonly SolidColorBrush WellBrush = B(Well);
        public static readonly SolidColorBrush LineBrush = B(Line);
        public static readonly SolidColorBrush EdgeBrush = B(Edge);

        private static FontFamily _display;
        public static FontFamily Display
        {
            get
            {
                if (_display == null)
                {
                    try { _display = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/#Michroma"); }
                    catch { _display = new FontFamily("Segoe UI Semibold"); }
                }
                return _display;
            }
        }

        public static readonly FontFamily Body = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private static ImageSource _appIcon;
        public static ImageSource AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    try { _appIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/KivoSpin.ico")); }
                    catch { }
                }
                return _appIcon;
            }
        }

        public static Brush PanelGradient()
        {
            var g = new LinearGradientBrush(PanelTop, PanelBottom, 90);
            g.Freeze();
            return g;
        }

        private static ImageBrush _brushed;
        /// <summary>Tileable horizontal brushed-metal streaks, very low alpha. Lay it over a gradient.</summary>
        public static ImageBrush Brushed
        {
            get
            {
                if (_brushed != null) return _brushed;
                const int W = 320, H = 320;
                var px = new byte[W * H * 4];
                var rnd = new Random(7);
                for (int y = 0; y < H; y++)
                {
                    int row = rnd.Next(-18, 19);
                    for (int x = 0; x < W; x++)
                    {
                        int v = Math.Clamp(128 + row + rnd.Next(-10, 11), 0, 255);
                        int i = (y * W + x) * 4;
                        px[i] = px[i + 1] = px[i + 2] = (byte)v;
                        px[i + 3] = 20;
                    }
                }
                var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
                bmp.Freeze();
                _brushed = new ImageBrush(bmp)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, W, H),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.None
                };
                _brushed.Freeze();
                return _brushed;
            }
        }

        // ----------------------------------------------------------- text helpers

        public static TextBlock Label(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = Display,
            FontSize = 9.5,
            Foreground = InkDimBrush,
            Margin = new Thickness(0, 14, 0, 6)
        };

        public static TextBlock Text(string text, double size = 13, Brush brush = null, bool wrap = true) => new TextBlock
        {
            Text = text,
            FontFamily = Body,
            FontSize = size,
            Foreground = brush ?? InkBrush,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };

        public static TextBlock Title(string text, double size = 18) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = Display,
            FontSize = size,
            Foreground = InkBrush
        };

        public static System.Windows.Controls.TextBox Input(string value = "", double width = double.NaN) => new System.Windows.Controls.TextBox
        {
            Text = value ?? "",
            Width = width,
            Background = WellBrush,
            Foreground = InkBrush,
            BorderBrush = LineBrush,
            CaretBrush = AccentBrush,
            SelectionBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 5, 7, 5),
            FontFamily = Body,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        public static Border Led(bool on, double size = 7) => new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = on ? AccentBrush : B(0x2C, 0x30, 0x36),
            Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 } : null,
            VerticalAlignment = VerticalAlignment.Center
        };

        public static Border Card(UIElement child, Thickness? padding = null) => new Border
        {
            Background = B(0x60, Colors.Black),
            BorderBrush = B(0x26, 0x29, 0x2E),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = padding ?? new Thickness(14, 10, 14, 12),
            Child = child
        };

        public static SolidColorBrush B(byte r, byte g, byte b) => B(Color.FromRgb(r, g, b));
    }
}
