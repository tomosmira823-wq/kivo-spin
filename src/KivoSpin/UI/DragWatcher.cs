using System.Windows.Threading;
using KivoSpin.Core;
using KivoSpin.Interop;

namespace KivoSpin.UI
{
    /// <summary>
    /// Watches the mouse and trigger key. When the left button is held, the mouse has moved (a drag), and the trigger
    /// key is down, it fires Trigger at the cursor. The wheel window then only reveals itself if a real file drag
    /// reaches it, so text selections and window moves are ignored.
    /// </summary>
    public class DragWatcher
    {
        public event Action<int, int> Trigger;
        public event Action Released;

        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        private bool _wasDown, _fired, _keyWasDown;
        private Native.POINT _downAt;

        public DragWatcher() { _timer.Tick += Tick; }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void Tick(object sender, EventArgs e)
        {
            if (AppSettings.Current.Paused)
            {
                _wasDown = false; _fired = false;
                return;
            }

            int button = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? Native.VK_RBUTTON : Native.VK_LBUTTON;
            bool down = Native.IsKeyDown(button);
            bool key = Native.IsKeyDown(AppSettings.Current.TriggerVk);
            Native.GetCursorPos(out var p);

            if (down && !_wasDown)
            {
                _downAt = p;
                _fired = false;
            }

            if (down && !_fired)
            {
                int dx = p.X - _downAt.X, dy = p.Y - _downAt.Y;
                bool dragging = dx * dx + dy * dy > 64;
                // Require the key to be pressed (or freshly held) during the drag.
                if (dragging && key)
                {
                    _fired = true;
                    Trigger?.Invoke(p.X, p.Y);
                }
            }

            if (!down && _wasDown)
            {
                _fired = false;
                Released?.Invoke();
            }

            _wasDown = down;
            _keyWasDown = key;
        }
    }
}
