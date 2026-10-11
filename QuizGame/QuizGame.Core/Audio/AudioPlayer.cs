using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;

namespace QuizGame.Audio
{
    public class AudioPlayer : IDisposable
    {
        private WaveOutEvent _out;
        private AudioFileReader _reader;
        private readonly object _lock = new object();

        public event EventHandler PlaybackStopped;

        public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;
        public double PositionSec => _reader?.CurrentTime.TotalSeconds ?? 0;
        public double DurationSec => _reader?.TotalTime.TotalSeconds ?? 0;

        public void Load(string path, double startSec = 0)
        {
            lock (_lock)
            {
                Stop();
                _reader = new AudioFileReader(path);
                if (startSec > 0 && startSec < _reader.TotalTime.TotalSeconds)
                    _reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                _out = new WaveOutEvent { DesiredLatency = 150 };
                _out.Init(_reader);
                _out.PlaybackStopped += (_, __) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Play() => _out?.Play();
        public void Pause() => _out?.Pause();
        public void Stop()
        {
            try { _out?.Stop(); } catch { }
            _out?.Dispose(); _out = null;
            _reader?.Dispose(); _reader = null;
        }

        public void Dispose() => Stop();
    }

    // Отдельный слой для коротких эффектов (start-round, countdown, score sounds).
    // Чтобы они не конфликтовали с основной дорожкой.
    public class SoundBank : IDisposable
    {
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>();
        private readonly List<WaveOutEvent> _active = new List<WaveOutEvent>();

        public void Set(string key, string path) { if (!string.IsNullOrEmpty(path)) _paths[key] = path; }

        public void Play(string key)
        {
            if (!_paths.TryGetValue(key, out var path)) return;
            if (!File.Exists(path)) return;

            var reader = new AudioFileReader(path);
            var wo = new WaveOutEvent();
            wo.Init(reader);
            wo.PlaybackStopped += (_, __) =>
            {
                lock (_active) _active.Remove(wo);
                wo.Dispose(); reader.Dispose();
            };
            lock (_active) _active.Add(wo);
            wo.Play();
        }

        public void Dispose()
        {
            lock (_active) foreach (var w in _active.ToList()) { try { w.Stop(); } catch { } w.Dispose(); }
            _active.Clear();
        }
    }
}
