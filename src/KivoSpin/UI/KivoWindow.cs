using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KivoSpin.UI
{
    /// <summary>Borderless Kivo panel window: brushed metal, corner screws, engraved title, LED.</summary>
    public class KivoWindow : Window
    {
        protected readonly Grid BodyHost = new Grid();
        protected readonly StackPanel TitleExtras = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _title;

        public KivoWindow(string title, double width, double height = double.NaN)
        {
            Title = "Kivo Spin – " + title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = Theme.AppIcon;
            Width = width + 28;
            if (double.IsNaN(height)) SizeToContent = SizeToContent.Height; else Height = height + 28;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var outer = new Grid { Margin = new Thickness(14) };
            outer.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.6, Color = Colors.Black };

            var panel = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = Theme.PanelGradient(),
                BorderBrush = Theme.B(0x45, 0x4A, 0x52),
                BorderThickness = new Thickness(1)
            };
            outer.Children.Add(panel);
            outer.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Theme.Brushed, IsHitTestVisible = false });
            // inner edge line for the machined look
            outer.Children.Add(new Border { CornerRadius = new CornerRadius(15), Margin = new Thickness(1), BorderBrush = Theme.B(0x10, Colors.White), BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false });

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // title bar
            var bar = new Grid { Background = Brushes.Transparent, Margin = new Thickness(24, 0, 14, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var led = Theme.Led(true, 7);
            led.Margin = new Thickness(0, 0, 10, 0);
            bar.Children.Add(led);
            _title = new TextBlock
            {
                Text = "KIVO SPIN  ·  " + title.ToUpperInvariant(),
                FontFamily = Theme.Display,
                FontSize = 10.5,
                Foreground = Theme.InkDimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_title, 1);
            bar.Children.Add(_title);
            Grid.SetColumn(TitleExtras, 2);
            bar.Children.Add(TitleExtras);
            var close = new KButton("", false, "") { Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            close.Click += () => Close();
            Grid.SetColumn(close, 3);
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
            layout.Children.Add(bar);

            BodyHost.Margin = new Thickness(26, 2, 26, 24);
            Grid.SetRow(BodyHost, 1);
            layout.Children.Add(BodyHost);
            outer.Children.Add(layout);

            // screws
            AddScrew(outer, HorizontalAlignment.Left, VerticalAlignment.Top, 20);
            AddScrew(outer, HorizontalAlignment.Right, VerticalAlignment.Top, 70);
            AddScrew(outer, HorizontalAlignment.Left, VerticalAlignment.Bottom, 135);
            AddScrew(outer, HorizontalAlignment.Right, VerticalAlignment.Bottom, 45);

            Content = outer;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private static void AddScrew(Grid g, HorizontalAlignment h, VerticalAlignment v, double angle)
        {
            g.Children.Add(new Screw(angle) { HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(8) });
        }

        protected void SetBody(UIElement element)
        {
            BodyHost.Children.Clear();
            BodyHost.Children.Add(element);
        }

        /// <summary>Footer row: left-aligned items and right-aligned items.</summary>
        protected static Grid Footer(IEnumerable<UIElement> left, IEnumerable<UIElement> right)
        {
            var g = new Grid { Margin = new Thickness(-4, 16, -4, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var e in left ?? Enumerable.Empty<UIElement>()) l.Children.Add(e);
            var r = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var e in right ?? Enumerable.Empty<UIElement>()) r.Children.Add(e);
            Grid.SetColumn(r, 1);
            g.Children.Add(l);
            g.Children.Add(r);
            return g;
        }
    }

    /// <summary>Simple Kivo-styled message / confirm box.</summary>
    public class KDialog : KivoWindow
    {
        private bool _result;

        private KDialog(string title, string message, string yes, string no) : base(title, 420)
        {
            Topmost = true;
            var sp = new StackPanel();
            sp.Children.Add(Theme.Text(message, 13.5, Theme.InkBrush));
            var buttons = new List<UIElement>();
            if (!string.IsNullOrEmpty(no))
            {
                var n = new KButton(no);
                n.Click += () => { _result = false; Close(); };
                buttons.Add(n);
            }
            var y = new KButton(yes, true);
            y.Click += () => { _result = true; Close(); };
            buttons.Add(y);
            sp.Children.Add(Footer(null, buttons));
            SetBody(sp);
        }

        public static bool Confirm(string title, string message, string yes = "Yes", string no = "Cancel")
        {
            var d = new KDialog(title, message, yes, no);
            d.ShowDialog();
            return d._result;
        }

        public static void Info(string title, string message) => Confirm(title, message, "OK", null);
    }
}
