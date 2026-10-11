using System;
using NAudio.Wave;
using System.IO;

namespace GuessMelody.Audio
{
    public class AudioEngine : IDisposable
    {
        private readonly object _lock = new object();

        private IWavePlayer _output;
        private AudioFileReader _reader;

        private double _stopAtSec = -1;
        private bool _loop;
        private int _volumePercent = 80;
        private System.Windows.Threading.DispatcherTimer _positionTimer;
        private DateTime _playStartedUtc;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public string CurrentFile { get; private set; }
        public double CurrentPositionSec =>
            _reader?.CurrentTime.TotalSeconds ?? 0;
        public double TotalDurationSec =>
            _reader?.TotalTime.TotalSeconds ?? 0;

        public event EventHandler PlaybackStopped;
        public event EventHandler PositionChanged;

        public AudioEngine()
        {
            _positionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Play(string path, double startSec, double durationSec, int volumePercent, bool loop)
        {
            Stop();

            lock (_lock)
            {
                try
                {
                    _reader = new AudioFileReader(path);

                    if (startSec > 0 && startSec < _reader.TotalTime.TotalSeconds)
                        _reader.CurrentTime = TimeSpan.FromSeconds(startSec);

                    _volumePercent = Math.Max(0, Math.Min(100, volumePercent));
                    _reader.Volume = _volumePercent / 100f;

                    _stopAtSec = durationSec > 0
                        ? _reader.CurrentTime.TotalSeconds + durationSec
                        : -1;
                    _loop = loop;

                    _output = new WaveOutEvent();
                    _output.Init(_reader);
                    _output.PlaybackStopped += OnOutputPlaybackStopped;
                    _output.Play();

                    IsPlaying = true;
                    IsPaused = false;
                    CurrentFile = path;
                    _playStartedUtc = DateTime.UtcNow;

                    _positionTimer.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("AudioEngine.Play error: " + ex.Message);
                    Stop();
                }
            }
        }

        private void OnOutputPlaybackStopped(object sender, StoppedEventArgs e)
        {
            // Вызывается, когда файл дошёл до конца или был остановлен
            if (_loop && _reader != null)
            {
                // Повторить фрагмент
                var startSec = _reader.CurrentTime.TotalSeconds - (_stopAtSec - _reader.CurrentTime.TotalSeconds);
                // Простая реализация: начать с того же старта
                // Более точный loop — пересоздание reader, но для MVP ок
                try
                {
                    _reader.Position = 0;
                    _output.Play();
                    return;
                }
                catch { }
            }

            IsPlaying = false;
            _positionTimer.Stop();
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (_output == null) return;
            _output.Pause();
            IsPaused = true;
            _positionTimer.Stop();
        }

        public void Resume()
        {
            if (_output == null) return;
            _output.Play();
            IsPaused = false;
            _positionTimer.Start();
        }

        public void Stop()
        {
            lock (_lock)
            {
                _positionTimer?.Stop();

                if (_output != null)
                {
                    try { _output.PlaybackStopped -= OnOutputPlaybackStopped; } catch { }
                    try { _output.Stop(); } catch { }
                    try { _output.Dispose(); } catch { }
                    _output = null;
                }
                if (_reader != null)
                {
                    try { _reader.Dispose(); } catch { }
                    _reader = null;
                }

                IsPlaying = false;
                IsPaused = false;
                CurrentFile = null;
            }
        }

        public void SetVolume(int percent)
        {
            _volumePercent = Math.Max(0, Math.Min(100, percent));
            if (_reader != null)
                _reader.Volume = _volumePercent / 100f;
        }

        /// <summary>Проверка окончания фрагмента. Вызывается извне (таймер).</summary>
        public void TickCheck()
        {
            if (_reader == null || !IsPlaying || IsPaused) return;

            if (_stopAtSec > 0 && _reader.CurrentTime.TotalSeconds >= _stopAtSec)
            {
                Stop();
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _positionTimer?.Stop();
            Stop();
        }
    }

    /// <summary>
    /// Отдельный экземпляр для прослушивания треков из вкладки «Папки».
    /// Не мешает игровому воспроизведению.
    /// </summary>
    public class PreviewEngine : AudioEngine
    {
    }

    public class BuzzerPlayer : IDisposable
    {
        private IWavePlayer _output;
        private AudioFileReader _reader;
        private readonly object _lock = new object();

        public void Play(string path, int volumePercent = 100)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            Stop();

            lock (_lock)
            {
                try
                {
                    _reader = new AudioFileReader(path)
                    {
                        Volume = Math.Max(0f, Math.Min(1f, volumePercent / 100f))
                    };
                    _output = new WaveOutEvent();
                    _output.Init(_reader);
                    _output.PlaybackStopped += (s, e) => Stop();
                    _output.Play();
                }
                catch
                {
                    Stop();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                try { _output?.Stop(); } catch { }
                try { _output?.Dispose(); } catch { }
                try { _reader?.Dispose(); } catch { }
                _output = null;
                _reader = null;
            }
        }

        public void Dispose() => Stop();
    }
}