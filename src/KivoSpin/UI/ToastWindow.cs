using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    public record ToastAction(string Label, Action Act, bool Primary = false);

    public static class Ui
    {
        public static void Post(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
        public static T Invoke<T>(Func<T> f) => Application.Current.Dispatcher.Invoke(f);
        public static void Invoke(Action a) => Application.Current.Dispatcher.Invoke(a);

        public static void ShowInFolder(string path)
        {
            try
            {
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("Explorer failed", ex); }
        }

        public static void Open(string pathOrUrl)
        {
            try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Open failed", ex); }
        }
    }

    /// <summary>Bottom-right notification panel. One at a time; a new toast replaces the old one.</summary>
    public class ToastWindow : Window
    {
        private static ToastWindow _current;

        private readonly TextBlock _title, _message;
        private readonly Border _bar, _barFill;
        private readonly StackPanel _actions;
        private readonly DispatcherTimer _life = new DispatcherTimer();
        private bool _sticky;

        private ToastWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 384;
            SizeToContent = SizeToContent.Height;
            UseLayoutRounding = true;

            var outer = new Grid { Margin = new Thickness(12) };
            outer.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.6, Color = Colors.Black };
            outer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = Theme.PanelGradient(),
                BorderBrush = Theme.B(0x45, 0x4A, 0x52),
                BorderThickness = new Thickness(1)
            });
            outer.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Theme.Brushed, IsHitTestVisible = false });

            var g = new Grid { Margin = new Thickness(16, 14, 14, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var led = Theme.Led(true, 8);
            led.VerticalAlignment = VerticalAlignment.Top;
            led.Margin = new Thickness(0, 5, 12, 0);
            g.Children.Add(led);

            var sp = new StackPanel();
            _title = new TextBlock { FontFamily = Theme.Display, FontSize = 10.5, Foreground = Theme.InkBrush, TextWrapping = TextWrapping.Wrap };
            _message = new TextBlock { FontFamily = Theme.Body, FontSize = 12.5, Foreground = Theme.InkDimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
            sp.Children.Add(_title);
            sp.Children.Add(_message);
            _bar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Theme.WellBrush, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            _barFill = new Border { CornerRadius = new CornerRadius(2.5), Background = Theme.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _bar.Child = _barFill;
            sp.Children.Add(_bar);
            _actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 8, 0, 0) };
            sp.Children.Add(_actions);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            var close = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 10, Foreground = Theme.InkFaintBrush, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(8, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            close.MouseLeftButtonUp += (s, e) => Dismiss();
            Grid.SetColumn(close, 2);
            g.Children.Add(close);
            outer.Children.Add(g);
            Content = outer;

            _life.Tick += (s, e) => { _life.Stop(); Dismiss(); };
            MouseEnter += (s, e) => _life.Stop();
            MouseLeave += (s, e) => { if (!_sticky) Restart(4); };
            SizeChanged += (s, e) => Place();
            Loaded += (s, e) => Place();
        }

        private void Place()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 6;
            Top = wa.Bottom - ActualHeight - 6;
        }

        private void Restart(double seconds)
        {
            _life.Stop();
            _life.Interval = TimeSpan.FromSeconds(seconds);
            _life.Start();
        }

        public void Dismiss()
        {
            _life.Stop();
            if (_current == this) _current = null;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fade);
        }

        public static ToastWindow Show(string title, string message, bool sticky = false, params ToastAction[] actions)
        {
            var t = Reuse();
            t.Set(title, message, null, sticky, actions);
            return t;
        }

        public static ToastWindow Progress(string title, string message)
        {
            var t = Reuse();
            t.Set(title, message, 0, true, null);
            return t;
        }

        private static ToastWindow Reuse()
        {
            if (_current != null && _current.IsLoaded) return _current;
            var t = new ToastWindow();
            _current = t;
            t.Opacity = 0;
            t.Show();
            t.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            return t;
        }

        /// <summary>Update in place. progress: null hides the bar, 0..1 shows it; double.NaN = indeterminate (half bar).</summary>
        public void Update(string title, string message, double? progress)
        {
            if (title != null) _title.Text = title.ToUpperInvariant();
            if (message != null) _message.Text = message;
            if (progress == null) _bar.Visibility = Visibility.Collapsed;
            else
            {
                _bar.Visibility = Visibility.Visible;
                double p = double.IsNaN(progress.Value) ? 0.5 : Math.Clamp(progress.Value, 0, 1);
                _bar.UpdateLayout();
                double full = Math.Max(0, _bar.ActualWidth > 0 ? _bar.ActualWidth : 300);
                _barFill.Width = full * p;
            }
        }

        public void Set(string title, string message, double? progress, bool sticky, ToastAction[] actions)
        {
            if (_current != this) { _current = this; }
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _sticky = sticky;
            Update(title ?? "", message ?? "", progress);
            _actions.Children.Clear();
            if (actions != null)
            {
                foreach (var a in actions.Where(a => a != null))
                {
                    var b = new KButton(a.Label, a.Primary) { Padding = new Thickness(11, 6, 11, 6) };
                    var act = a.Act;
                    b.Click += () => { Dismiss(); act?.Invoke(); };
                    _actions.Children.Add(b);
                }
            }
            _actions.Visibility = _actions.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sticky) _life.Stop(); else Restart(actions != null && actions.Length > 0 ? 9 : 5);
        }
    }
}
