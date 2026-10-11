using System;
using System.Windows.Threading;

namespace GuessMelody.Core
{
    /// <summary>Тикает раз в 100 мс, сообщает оставшееся время.</summary>
    public class RoundTimer : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private DateTime _deadlineUtc;
        private bool _running;

        public event EventHandler<double> Tick;      // остаток в секундах
        public event EventHandler Timeout;

        public RoundTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += OnTick;
        }

        public bool IsRunning => _running;

        public void Start(double seconds)
        {
            _deadlineUtc = DateTime.UtcNow.AddSeconds(seconds);
            _running = true;
            _timer.Start();
        }

        public void Stop()
        {
            _running = false;
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_running) return;
            var left = (_deadlineUtc - DateTime.UtcNow).TotalSeconds;
            if (left <= 0)
            {
                Stop();
                Tick?.Invoke(this, 0);
                Timeout?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Tick?.Invoke(this, left);
            }
        }

        public void Dispose() => Stop();
    }
}