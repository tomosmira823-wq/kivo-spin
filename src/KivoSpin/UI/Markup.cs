using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    public enum AnnotKind { Pen, Highlighter, Arrow, Rect, Ellipse, Text, Redact }

    public class Annot
    {
        public AnnotKind Kind;
        public List<Point> Pts = new List<Point>();  // in page pixel coordinates
        public Color Color;
        public double Width;
        public string Text;
        public double FontSize;
    }

    public static class AnnotRenderer
    {
        public static void Draw(DrawingContext dc, IEnumerable<Annot> list, double scale)
        {
            foreach (var a in list) DrawOne(dc, a, scale);
        }

        public static void DrawOne(DrawingContext dc, Annot a, double scale)
        {
            if (a.Pts.Count == 0) return;
            var pts = a.Pts.Select(p => new Point(p.X * scale, p.Y * scale)).ToList();
            double w = a.Width * scale;
            var brush = new SolidColorBrush(a.Color); brush.Freeze();
            switch (a.Kind)
            {
                case AnnotKind.Pen:
                case AnnotKind.Highlighter:
                    {
                        var c = a.Kind == AnnotKind.Highlighter ? Color.FromArgb(0x70, a.Color.R, a.Color.G, a.Color.B) : a.Color;
                        var pb = new SolidColorBrush(c); pb.Freeze();
                        var pen = new Pen(pb, a.Kind == AnnotKind.Highlighter ? w * 3.2 : w)
                        {
                            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                        };
                        if (a.Kind == AnnotKind.Highlighter) { pen.StartLineCap = PenLineCap.Flat; pen.EndLineCap = PenLineCap.Flat; }
                        if (pts.Count == 1) { dc.DrawEllipse(pb, null, pts[0], pen.Thickness / 2, pen.Thickness / 2); break; }
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(pts[0], false, false);
                            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
                        }
                        g.Freeze();
                        dc.DrawGeometry(null, pen, g);
                        break;
                    }
                case AnnotKind.Arrow:
                    {
                        if (pts.Count < 2) break;
                        Point p0 = pts[0], p1 = pts[pts.Count - 1];
                        var pen = new Pen(brush, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        var v = p1 - p0;
                        double len = v.Length;
                        if (len < 1) break;
                        v /= len;
                        double head = Math.Max(w * 4.2, 10 * scale);
                        var basePt = p1 - v * head;
                        dc.DrawLine(pen, p0, basePt);
                        var n = new Vector(-v.Y, v.X);
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(p1, true, true);
                            ctx.LineTo(basePt + n * head * 0.55, true, true);
                            ctx.LineTo(basePt - n * head * 0.55, true, true);
                        }
                        g.Freeze();
                        dc.DrawGeometry(brush, null, g);
                        break;
                    }
                case AnnotKind.Rect:
                    if (pts.Count >= 2) dc.DrawRectangle(null, new Pen(brush, w) { LineJoin = PenLineJoin.Round }, new Rect(pts[0], pts[pts.Count - 1]));
                    break;
                case AnnotKind.Ellipse:
                    if (pts.Count >= 2)
                    {
                        var r = new Rect(pts[0], pts[pts.Count - 1]);
                        dc.DrawEllipse(null, new Pen(brush, w), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                    }
                    break;
                case AnnotKind.Redact:
                    if (pts.Count >= 2) dc.DrawRectangle(Brushes.Black, null, new Rect(pts[0], pts[pts.Count - 1]));
                    break;
                case AnnotKind.Text:
                    if (!string.IsNullOrEmpty(a.Text))
                    {
                        var ft = new FormattedText(a.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface(Theme.Body, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), Math.Max(1, a.FontSize * scale), brush, 1.0);
                        dc.DrawText(ft, pts[0]);
                    }
                    break;
            }
        }
    }

    /// <summary>Canvas that shows and records annotations for one page (coordinates = page pixels).</summary>
    public class AnnotCanvas : FrameworkElement
    {
        public List<Annot> Items = new List<Annot>();
        public Annot Live;
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            AnnotRenderer.Draw(dc, Items, 1);
            if (Live != null) AnnotRenderer.DrawOne(dc, Live, 1);
        }
    }

    /// <summary>Annotate or redact images and PDF pages.</summary>
    public class MarkupWindow : KivoWindow
    {
        public bool Saved { get; private set; }
        public Dictionary<int, List<Annot>> Pages { get; } = new Dictionary<int, List<Annot>>();

        private readonly bool _redact;
        private readonly Func<int, BitmapSource> _pageLoader;
        private readonly int _pageCount;
        private int _page;
        private readonly Grid _stage = new Grid();
        private readonly Image _base = new Image { Stretch = Stretch.Fill };
        private readonly AnnotCanvas _canvas = new AnnotCanvas();
        private readonly Canvas _textLayer = new Canvas();
        private readonly Viewbox _view;
        private readonly TextBlock _pageLabel;
        private AnnotKind _tool;
        private Color _color = Color.FromRgb(0xFF, 0x3B, 0x30);
        private double _sizeFactor = 1.0;
        private readonly List<Border> _swatches = new List<Border>();
        private TextBox _editing;

        public MarkupWindow(bool redact, string fileName, int pageCount, Func<int, BitmapSource> pageLoader)
            : base((redact ? "Redact" : "Annotate") + "  ·  " + fileName, 980)
        {
            _redact = redact;
            _pageCount = pageCount;
            _pageLoader = pageLoader;
            _tool = redact ? AnnotKind.Redact : AnnotKind.Pen;

            var root = new StackPanel();

            // toolbar
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (!redact)
            {
                var tools = new[] { ("Pen", AnnotKind.Pen), ("Marker", AnnotKind.Highlighter), ("Arrow", AnnotKind.Arrow), ("Box", AnnotKind.Rect), ("Circle", AnnotKind.Ellipse), ("Text", AnnotKind.Text) };
                var seg = new KSegment(tools.Select(t => t.Item1), 0) { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                seg.Changed += i => { CommitText(); _tool = tools[i].Item2; };
                bar.Children.Add(seg);

                var colors = new[] { Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0xD6, 0x0A), Theme.Accent, Color.FromRgb(0x0A, 0x84, 0xFF), Colors.Black, Colors.White };
                var sw = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                foreach (var c in colors)
                {
                    var cc = c;
                    var b = new Border
                    {
                        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(3, 0, 3, 0),
                        Background = Theme.B(cc), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = cc
                    };
                    b.MouseLeftButtonUp += (s, e) => { _color = cc; PaintSwatches(); if (_editing != null) _editing.Foreground = Theme.B(cc); };
                    _swatches.Add(b);
                    sw.Children.Add(b);
                }
                bar.Children.Add(sw);
                PaintSwatches();

                var size = new KSegment(new[] { "S", "M", "L" }, 1) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                size.Changed += i => _sizeFactor = i == 0 ? 0.55 : i == 1 ? 1.0 : 1.8;
                bar.Children.Add(size);
            }
            else
            {
                var hint = Theme.Text("Drag boxes over anything you want hidden. Redacted pages are flattened, so the text underneath can't be copied out.", 12.5, Theme.InkDimBrush);
                hint.MaxWidth = 640;
                hint.VerticalAlignment = VerticalAlignment.Center;
                hint.Margin = new Thickness(0, 0, 12, 0);
                bar.Children.Add(hint);
            }
            var undo = new KButton("Undo", false, "");
            undo.Click += Undo;
            var clear = new KButton("Clear page", false, "");
            clear.Click += () => { CommitText(); _canvas.Items.Clear(); _canvas.InvalidateVisual(); };
            bar.Children.Add(undo);
            bar.Children.Add(clear);
            root.Children.Add(bar);

            // stage
            _base.IsHitTestVisible = false;
            _stage.Children.Add(_base);
            _stage.Children.Add(_canvas);
            _stage.Children.Add(_textLayer);
            _view = new Viewbox { Stretch = Stretch.Uniform, Child = _stage, StretchDirection = StretchDirection.Both };
            root.Children.Add(new Border
            {
                Background = Theme.WellBrush, BorderBrush = Theme.EdgeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Height = 540, Child = _view
            });

            _canvas.Cursor = Cursors.Cross;
            _canvas.MouseLeftButtonDown += CanvasDown;
            _canvas.MouseMove += CanvasMove;
            _canvas.MouseLeftButtonUp += CanvasUp;

            // footer
            var left = new List<UIElement>();
            _pageLabel = new TextBlock { FontFamily = Theme.Display, FontSize = 10, Foreground = Theme.InkDimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            if (pageCount > 1)
            {
                var prev = new KButton("", false, "");
                prev.Click += () => GoTo(_page - 1);
                var next = new KButton("", false, "");
                next.Click += () => GoTo(_page + 1);
                left.Add(prev); left.Add(_pageLabel); left.Add(next);
            }
            var cancel = new KButton("Cancel");
            cancel.Click += () => Close();
            var save = new KButton("Save copy", true, "");
            save.Click += () => { CommitText(); Store(); Saved = Pages.Values.Any(l => l.Count > 0); Close(); };
            root.Children.Add(Footer(left, new UIElement[] { cancel, save }));
            SetBody(root);

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _editing == null) { Undo(); e.Handled = true; }
            };
            GoTo(0);
        }

        private void PaintSwatches()
        {
            foreach (var b in _swatches)
                b.BorderBrush = (Color)b.Tag == _color ? Theme.AccentBrush : Theme.B(0x55, 0x5A, 0x62);
        }

        private double DocLong => Math.Max(_stage.Width, _stage.Height);
        private double StrokeWidth => Math.Max(2, DocLong / 260) * _sizeFactor;

        private void Store()
        {
            Pages[_page] = _canvas.Items.ToList();
        }

        private void GoTo(int page)
        {
            if (page < 0 || page >= _pageCount) return;
            CommitText();
            if (_base.Source != null) Store();
            _page = page;
            BitmapSource bmp;
            try { bmp = _pageLoader(page); }
            catch (Exception ex) { Log.Error("Page load failed", ex); return; }
            if (bmp == null) return;
            _base.Source = bmp;
            _stage.Width = bmp.PixelWidth; _stage.Height = bmp.PixelHeight;
            _base.Width = bmp.PixelWidth; _base.Height = bmp.PixelHeight;
            _canvas.Width = bmp.PixelWidth; _canvas.Height = bmp.PixelHeight;
            _textLayer.Width = bmp.PixelWidth; _textLayer.Height = bmp.PixelHeight;
            _canvas.Items = Pages.TryGetValue(page, out var list) ? list.ToList() : new List<Annot>();
            _canvas.InvalidateVisual();
            _pageLabel.Text = $"PAGE {page + 1} / {_pageCount}";
        }

        private void Undo()
        {
            if (_editing != null) { CancelText(); return; }
            if (_canvas.Items.Count > 0) { _canvas.Items.RemoveAt(_canvas.Items.Count - 1); _canvas.InvalidateVisual(); }
        }

        private void CanvasDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_tool == AnnotKind.Text)
            {
                CommitText();
                StartText(p);
                e.Handled = true;
                return;
            }
            CommitText();
            _canvas.Live = new Annot { Kind = _tool, Color = _redact ? Colors.Black : _color, Width = StrokeWidth };
            _canvas.Live.Pts.Add(p);
            if (_tool != AnnotKind.Pen && _tool != AnnotKind.Highlighter) _canvas.Live.Pts.Add(p);
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        private void CanvasMove(object sender, MouseEventArgs e)
        {
            if (_canvas.Live == null || !_canvas.IsMouseCaptured) return;
            var p = e.GetPosition(_canvas);
            p = new Point(Math.Clamp(p.X, 0, _canvas.Width), Math.Clamp(p.Y, 0, _canvas.Height));
            if (_canvas.Live.Kind == AnnotKind.Pen || _canvas.Live.Kind == AnnotKind.Highlighter)
            {
                var last = _canvas.Live.Pts[_canvas.Live.Pts.Count - 1];
                if ((p - last).Length >= Math.Max(1, StrokeWidth * 0.3)) _canvas.Live.Pts.Add(p);
            }
            else _canvas.Live.Pts[1] = p;
            _canvas.InvalidateVisual();
        }

        private void CanvasUp(object sender, MouseButtonEventArgs e)
        {
            if (_canvas.Live == null) return;
            _canvas.ReleaseMouseCapture();
            var a = _canvas.Live;
            _canvas.Live = null;
            bool tiny = a.Pts.Count >= 2 && (a.Pts[a.Pts.Count - 1] - a.Pts[0]).Length < 3 && a.Kind != AnnotKind.Pen && a.Kind != AnnotKind.Highlighter;
            if (!tiny) _canvas.Items.Add(a);
            _canvas.InvalidateVisual();
        }

        private void StartText(Point p)
        {
            double fs = Math.Max(14, DocLong / 42) * _sizeFactor;
            _editing = new TextBox
            {
                FontFamily = Theme.Body, FontWeight = FontWeights.SemiBold, FontSize = fs,
                Foreground = Theme.B(_color), Background = Theme.B(0x40, Colors.Black), BorderBrush = Theme.AccentBrush,
                BorderThickness = new Thickness(Math.Max(1, fs / 14)), MinWidth = fs * 4, Padding = new Thickness(0),
                AcceptsReturn = true, Tag = p, CaretBrush = Theme.AccentBrush
            };
            Canvas.SetLeft(_editing, p.X);
            Canvas.SetTop(_editing, p.Y);
            _textLayer.Children.Add(_editing);
            _editing.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
                else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { CommitText(); e.Handled = true; }
            };
            _editing.Loaded += (s, e) => { _editing?.Focus(); };
        }

        private void CommitText()
        {
            if (_editing == null) return;
            var tb = _editing;
            _editing = null;
            _textLayer.Children.Remove(tb);
            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                var p = (Point)tb.Tag;
                var color = ((SolidColorBrush)tb.Foreground).Color;
                // TextBox has a little internal padding; nudge so the text lands where it was typed.
                var a = new Annot { Kind = AnnotKind.Text, Color = color, Text = tb.Text, FontSize = tb.FontSize };
                a.Pts.Add(new Point(p.X + 2 + tb.BorderThickness.Left, p.Y + tb.BorderThickness.Top));
                _canvas.Items.Add(a);
                _canvas.InvalidateVisual();
            }
        }

        private void CancelText()
        {
            if (_editing == null) return;
            _textLayer.Children.Remove(_editing);
            _editing = null;
        }
    }
}
