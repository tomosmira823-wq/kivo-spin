using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    public class WelcomeWindow : KivoWindow
    {
        public WelcomeWindow() : base("Welcome", 640)
        {
            Topmost = true;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // live mini wheel
            var demo = new WheelVisual { R = 92, Compact = true, Revealed = true, Hover = 0, HubTitle = "CONVERT", HubSub = "hold → tools", Caption = "Photo.heic  → PNG" };
            demo.Items = WheelPages.Convert(new[] { "Photo.heic" }).Where(i => i.Enabled).Take(8).ToList();
            demo.Width = demo.TotalWidth; demo.Height = demo.TotalHeight;
            demo.HorizontalAlignment = HorizontalAlignment.Center;
            demo.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(demo);
            int step = 0;
            var spin = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            spin.Tick += (s, e) =>
            {
                step = (step + 1) % demo.Items.Count;
                demo.Hover = step;
                demo.Caption = "Photo.heic  " + demo.Items[step].Verb;
                demo.InvalidateVisual();
            };
            spin.Start();
            Closed += (s, e) => spin.Stop();

            var right = new StackPanel { Margin = new Thickness(18, 4, 0, 0) };
            Grid.SetColumn(right, 1);
            right.Children.Add(Theme.Title("Kivo Spin", 22));
            var tag = Theme.Text("Convert and fix files without opening anything.", 13.5, Theme.InkDimBrush);
            tag.Margin = new Thickness(0, 6, 0, 14);
            right.Children.Add(tag);

            string key = AppSettings.Current.TriggerName;
            right.Children.Add(Step(1, "Start dragging any file", "Photos, PDFs, Word docs – from File Explorer or your desktop."));
            right.Children.Add(Step(2, $"Hold {key}", "The wheel pops up under your cursor."));
            right.Children.Add(Step(3, "Drop it on a wedge", "Your new file appears next to the original. The original is never changed."));
            right.Children.Add(Step(4, "Hover the middle for tools", "Compress, crop, remove backgrounds, redact, annotate and more."));
            grid.Children.Add(right);

            var root = new StackPanel();
            root.Children.Add(grid);

            var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            keyRow.Children.Add(new TextBlock { Text = "TRIGGER KEY", FontFamily = Theme.Display, FontSize = 9.5, Foreground = Theme.InkDimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var seg = new KSegment(new[] { "Shift", "Ctrl", "Alt" }, (int)AppSettings.Current.Trigger);
            seg.Changed += i => { AppSettings.Current.Trigger = (TriggerKey)i; AppSettings.Save(); };
            keyRow.Children.Add(seg);
            root.Children.Add(keyRow);

            var note = Theme.Text("Kivo Spin lives in the tray (bottom-right, by the clock). Right-click it for settings.", 12, Theme.InkFaintBrush);
            note.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(note);

            var go = new KButton("Let's go", true, "");
            go.Click += () => Close();
            root.Children.Add(Footer(null, new UIElement[] { go }));
            SetBody(root);
        }

        private static UIElement Step(int n, string title, string body)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top,
                BorderBrush = Theme.AccentBrush, BorderThickness = new Thickness(1.5), Background = Theme.B(0x22, Theme.Accent),
                Child = new TextBlock { Text = n.ToString(), FontFamily = Theme.Display, FontSize = 10, Foreground = Theme.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            g.Children.Add(badge);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontFamily = Theme.Body, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Theme.InkBrush });
            sp.Children.Add(Theme.Text(body, 12.5, Theme.InkDimBrush));
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            return g;
        }
    }
}
