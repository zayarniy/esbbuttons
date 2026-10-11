using System;
using System.IO;
using System.Windows.Threading;
using GuessMelody.Audio;
using GuessMelody.Core;
using GuessMelody.Core.Models;

namespace GuessMelody.Services
{
    public class AudioCoordinator : IDisposable
    {
        private readonly GameEngine _engine;
        private readonly AudioEngine _audio;
        private readonly BuzzerPlayer _buzzer;
        private readonly LogService _log;
        private readonly DispatcherTimer _playbackTimer;
        private DateTime _startUtc;
        private double _duration;
        private double _pauseStartedUtcSeconds;   // когда поставили на паузу
        private double _pauseStartedSec;
        private bool _isPaused;
        private string _lastFile;
        private double _lastPositionSec;
        private int _lastVolume = 80;
        private bool _lastLoop;

        public string RootFolder { get; set; } = "";

        public AudioCoordinator(GameEngine engine, AudioEngine audio, BuzzerPlayer buzzer, LogService log)
        {
            _engine = engine;
            _audio = audio;
            _buzzer = buzzer;
            _log = log;

            _engine.RequestPlayTrack += OnPlay;
            _engine.RequestStopTrack += OnStop;
            _engine.RequestPauseTrack += OnPause;
            _engine.RequestResumeTrack += OnResume;

            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _playbackTimer.Tick += OnPlaybackTick;
        }

        private void OnPlay(object sender, Track track)
        {
            try
            {
                var path = Path.IsPathRooted(track.RelativePath)
                    ? track.RelativePath
                    : Path.Combine(RootFolder ?? "", track.RelativePath);

                if (!File.Exists(path))
                {
                    _log.Add(Core.Enums.LogKind.Error, $"Файл не найден: {path}");
                    return;
                }


                var s = _engine.Settings;
                var ov = track.Overrides;

                double startSec = ov?.StartSec ?? s.Rules.DefaultStartSec;
                if ((ov?.RandomStart ?? s.Rules.RandomStart) && track.DurationSec > 0)
                {
                    // старт со случайного места, но не ближе к концу, чем длительность фрагмента
                    double frag = ov?.DurationSec ?? s.Rules.FragmentSec;
                    double max = Math.Max(0, track.DurationSec - frag);
                    startSec = max > 0 ? new Random().NextDouble() * max : 0;
                }

                double durationSec = ov?.DurationSec ?? s.Rules.FragmentSec;
                int volume = ov?.Volume ?? s.Audio.Volume;
                bool loop = ov?.Loop ?? false;

                _audio.Play(path, startSec, durationSec, volume, loop);
                _duration = durationSec;
                _startUtc = DateTime.UtcNow;
                _lastFile = path;
                _lastVolume = volume;
                _lastLoop = loop;
                _lastPositionSec = startSec;
                _playbackTimer.Start();
                _log.Add(Core.Enums.LogKind.Audio, $"▶ {Path.GetFileName(path)} " +
                    $"[start={startSec:F1}s, dur={durationSec}s, vol={volume}%]");
            }
            catch (Exception ex)
            {
                _log.Add(Core.Enums.LogKind.Error, "Audio play error: " + ex.Message);
            }
        }

        private void OnStop(object sender, EventArgs e)
        {
            _audio.Stop();
            _playbackTimer.Stop();
            _isPaused = false;
            _lastFile = null;
            _lastPositionSec = 0;
            _log.Add(Core.Enums.LogKind.Audio, "⏹ Стоп");
        }

        private void OnPause(object sender, EventArgs e)
        {
            _audio.Pause();

            if (_audio.IsPaused)
            {
                _playbackTimer.Stop();
                _pauseStartedSec = (DateTime.UtcNow - _startUtc).TotalSeconds;
                _isPaused = true;
                _log.Add(Core.Enums.LogKind.Audio, "⏸ Пауза");
            }
            else
            {
                // Не смогли поставить на паузу (уже остановлено/нет файла) — тихо игнор
                _log.Add(Core.Enums.LogKind.Audio, "⏸ Пауза (нечего ставить)");
            }
        }

        private void OnResume(object sender, EventArgs e)
        {
            // Первый случай — пауза была, продолжаем с той же позиции
            if (_isPaused)
            {
                _audio.Resume();

                if (!_audio.IsPaused)   // успешно возобновили
                {
                    _startUtc = DateTime.UtcNow.AddSeconds(-_pauseStartedSec);
                    _playbackTimer.Start();
                    _isPaused = false;
                    _log.Add(Core.Enums.LogKind.Audio, "▶ Продолжение");
                }
                else
                {
                    _log.Add(Core.Enums.LogKind.Error, "▶ Не удалось продолжить воспроизведение");
                }
                return;
            }

            // Второй случай — файл был остановлен полностью (после Stop).
            // Восстанавливаем воспроизведение с последней известной позиции.
            if (!string.IsNullOrEmpty(_lastFile))
            {
                try
                {
                    var resumeFromSec = _lastPositionSec;
                    if (resumeFromSec < 0) resumeFromSec = 0;

                    // Сколько осталось играть — вычисляем из сохранённой длительности
                    var remaining = _duration - resumeFromSec;
                    if (remaining <= 0) remaining = _duration;

                    _audio.Play(_lastFile, resumeFromSec, remaining, _lastVolume, _lastLoop);
                    _duration = remaining;
                    _startUtc = DateTime.UtcNow;
                    _playbackTimer.Start();
                    _log.Add(Core.Enums.LogKind.Audio,
                        $"▶ Восстановлено воспроизведение с {resumeFromSec:F1} с");
                }
                catch (Exception ex)
                {
                    _log.Add(Core.Enums.LogKind.Error, "Resume error: " + ex.Message);
                }
            }
        }
        private void OnPlaybackTick(object sender, EventArgs e)
        {
            if (!_audio.IsPlaying && !_audio.IsPaused) { _playbackTimer.Stop(); return; }

            var elapsed = (DateTime.UtcNow - _startUtc).TotalSeconds;
            var left = Math.Max(0, _duration - elapsed);
            _engine.TickPlayback(left);

            _lastPositionSec = elapsed;   // ← запомнили текущую позицию

            if (left <= 0)
            {
                _playbackTimer.Stop();
                _audio.Stop();
                _log.Add(Core.Enums.LogKind.Audio, "⏱ Фрагмент закончился");
            }
        }

        public void PlayBuzzer()
        {
            var path = _engine?.Settings?.Audio?.BuzzerPath;
            if (!string.IsNullOrWhiteSpace(path))
                _buzzer.Play(path, _engine.Settings.Audio.Volume);
        }

        public void PlayCorrect()
        {
            var path = _engine?.Settings?.Audio?.CorrectPath;
            if (!string.IsNullOrWhiteSpace(path))
                _buzzer.Play(path, _engine.Settings.Audio.Volume);
        }

        public void PlayWrong()
        {
            var path = _engine?.Settings?.Audio?.WrongPath;
            if (!string.IsNullOrWhiteSpace(path))
                _buzzer.Play(path, _engine.Settings.Audio.Volume);
        }

        public void Dispose()
        {
            _playbackTimer?.Stop();
            _engine.RequestPlayTrack -= OnPlay;
            _engine.RequestStopTrack -= OnStop;
            _engine.RequestPauseTrack -= OnPause;
            _engine.RequestResumeTrack -= OnResume;
        }
    }
}