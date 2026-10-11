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

        private TimeSpan _pausedPosition = TimeSpan.Zero;
        private bool _paused = false;


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
            lock (_lock)
            {
                if (_output == null || _reader == null) return;
                if (!IsPlaying || _paused) return;

                try
                {
                    _output.Pause();
                    _pausedPosition = _reader.CurrentTime;
                    _paused = true;
                    IsPaused = true;
                    _positionTimer?.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("AudioEngine.Pause error: " + ex.Message);
                }
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (_output == null || _reader == null)
                {
                    // Плеер был полностью остановлен — ничего не сделаем на этом уровне,
                    // координатор должен позаботиться о перезапуске.
                    return;
                }

                try
                {
                    if (_paused)
                    {
                        // Продолжение на том же WaveOutEvent
                        _output.Play();
                        _paused = false;
                        IsPaused = false;
                        IsPlaying = true;
                        _positionTimer?.Start();
                        return;
                    }

                    // Резервный путь: если _paused по какой-то причине false,
                    // но воспроизведение стоит — попробуем восстановить с _pausedPosition.
                    if (!IsPlaying && _reader != null)
                    {
                        if (_pausedPosition > TimeSpan.Zero &&
                            _pausedPosition < _reader.TotalTime)
                        {
                            _reader.CurrentTime = _pausedPosition;
                        }
                        _output.Play();
                        IsPlaying = true;
                        IsPaused = false;
                        _positionTimer?.Start();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("AudioEngine.Resume error: " + ex.Message);
                }
            }
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
                _paused = false;
                _pausedPosition = TimeSpan.Zero;
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