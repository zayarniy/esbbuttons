//using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GuessMelody.Core.Audio
{
    /// <summary>
    /// Реализация IAudioEngine на NAudio.
    /// - один "фоновый" трек (mp3/wav/ogg) с паузой/seek
    /// - one-shot звуки поверх (тик, right/wrong) через микшер
    /// </summary>
    public sealed class NaAudioEngine : IAudioEngine
    {
        // --- Фоновое воспроизведение ---
        private WaveOutEvent _output;
        private WaveStream _reader;
        private VolumeSampleProvider _volumeProvider;
        private MixingSampleProvider _mixer;

        // --- Состояние ---
        private float _volume = 1.0f;
        private readonly object _lock = new object();

        public event EventHandler TrackEnded;

        public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
        public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0f, Math.Min(1f, value));
                if (_volumeProvider != null)
                    _volumeProvider.Volume = _volume;
            }
        }

        public NaAudioEngine()
        {
            EnsureOutput();
        }

        // =============================================================
        // Загрузка
        // =============================================================
        public void Load(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException("Путь к файлу пуст.", nameof(file));
            if (!File.Exists(file))
                throw new FileNotFoundException("Файл не найден", file);

            lock (_lock)
            {
                // 1. Полностью останавливаем и освобождаем предыдущий output
                try { _output?.Stop(); } catch { }
                try { _output?.Dispose(); } catch { }
                _output = null;

                try { _reader?.Dispose(); } catch { }
                _reader = null;

                _mixer = null;
                _volumeProvider = null;

                // 2. Создаём новый reader и микшер
                _reader = CreateReader(file);
                _volumeProvider = new VolumeSampleProvider(_reader.ToSampleProvider())
                {
                    Volume = _volume
                };

                _mixer = new MixingSampleProvider(
                    WaveFormat.CreateIeeeFloatWaveFormat(
                        _reader.WaveFormat.SampleRate,
                        _reader.WaveFormat.Channels))
                {
                    ReadFully = true
                };
                _mixer.AddMixerInput(_volumeProvider);

                // 3. Создаём новый WaveOutEvent и инициализируем его ТОЛЬКО один раз
                _output = new WaveOutEvent { DesiredLatency = 100 };
                _output.PlaybackStopped += Output_PlaybackStopped;
                _output.Init(_mixer);
            }
        }

        public void Play()
        {
            lock (_lock)
            {
                if (_output == null || _reader == null) return;

                // Если дошли до конца — в начало
                if (_reader.Position >= _reader.Length - 1)
                    _reader.Position = 0;

                // Если уже играет — не трогаем
                if (_output.PlaybackState == PlaybackState.Playing) return;

                _output.Play();
            }
        }

        private WaveStream CreateReader(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            switch (ext)
            {
                //case ".ogg":
                //    return new VorbisWaveReader(file);

                case ".mp3":
                case ".wav":
                case ".aiff":
                case ".m4a":
                case ".aac":
                case ".flac":       // flac — только если MediaFoundation его поддерживает
                    return new AudioFileReader(file);

                default:
                    throw new NotSupportedException($"Формат {ext} не поддерживается.");
            }
        }

        private void EnsureOutput()
        {
            if (_output != null) return;
            _output = new WaveOutEvent { DesiredLatency = 100 };
            _output.PlaybackStopped += Output_PlaybackStopped;
        }

        // =============================================================
        // Управление
        // =============================================================


        public void Pause()
        {
            lock (_lock)
            {
                if (_output?.PlaybackState == PlaybackState.Playing)
                    _output.Pause();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            try { _output?.Stop(); } catch { }
            if (_reader != null)
            {
                try { _reader.Position = 0; } catch { }
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_lock)
            {
                if (_reader == null) return;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (position > _reader.TotalTime) position = _reader.TotalTime;
                _reader.CurrentTime = position;
            }
        }

        // =============================================================
        // One-shot: накладываем поверх через микшер
        // =============================================================
        public void PlayOneShot(string file, float volume = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return;

            Task.Run(() =>
            {
                WaveOutEvent oneShot = null;
                WaveStream reader = null;
                try
                {
                    reader = CreateReader(file);
                    var sp = reader.ToSampleProvider();
                    var withVolume = new VolumeSampleProvider(sp) { Volume = volume };

                    oneShot = new WaveOutEvent { DesiredLatency = 100 };
                    oneShot.Init(withVolume);
                    oneShot.Play();

                    // Ждём завершения, потом освобождаем
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (oneShot.PlaybackState == PlaybackState.Playing &&
                           sw.ElapsedMilliseconds < 10_000)
                    {
                        System.Threading.Thread.Sleep(20);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[NaAudioEngine] OneShot error: {ex.Message}");
                }
                finally
                {
                    try { oneShot?.Stop(); } catch { }
                    try { oneShot?.Dispose(); } catch { }
                    try { reader?.Dispose(); } catch { }
                }
            });
        }
        // =============================================================
        // События
        // =============================================================
        //private void Reader_PositionChanged(object sender, EventArgs e)
        //{
        //    if (_reader == null) return;
        //    if (_reader.Position >= _reader.Length - 1)
        //        TrackEnded?.Invoke(this, EventArgs.Empty);
        //}


        private void Output_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            // Ошибка воспроизведения
            if (e.Exception != null)
            {
                Console.Error.WriteLine($"[NaAudioEngine] Playback error: {e.Exception.Message}");
                return;
            }

            // Если проигрывание остановилось из-за достижения конца трека —
            // поднимаем TrackEnded. Проверяем по позиции ридера.
            if (_reader != null)
            {
                // Небольшой допуск: позиция может чуть-чуть не дойти до конца.
                var remaining = _reader.TotalTime - _reader.CurrentTime;
                if (remaining <= TimeSpan.FromMilliseconds(500))
                {
                    TrackEnded?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        // =============================================================
        // Dispose
        // =============================================================
        public void Dispose()
        {
            lock (_lock)
            {
                try { _output?.Stop(); } catch { }
                try { _output?.Dispose(); } catch { }
                _output = null;

                try { _reader?.Dispose(); } catch { }
                _reader = null;

                _mixer = null;
                _volumeProvider = null;
            }
        }

   //     public TimeSpan PlayOneShotAndWait(string file, float volume = 1.0f, int maxWaitMs = 5000)
        //{
        //    Console.WriteLine($"[Audio] OneShotAndWait {file}");
        //    return TimeSpan.FromMilliseconds(200);   // имитируем короткую задержку
        //}

        /// <summary>
        /// Проиграть короткий звук и дождаться его окончания.
        /// Возвращает фактическое время воспроизведения.
        /// </summary>
        public TimeSpan PlayOneShotAndWait(string file, float volume = 1.0f,
                                           int maxWaitMs = 5000)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return TimeSpan.Zero;

            WaveStream reader = null;
            WaveOutEvent oneShot = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                reader = CreateReader(file);
                var sp = reader.ToSampleProvider();
                var withVolume = new VolumeSampleProvider(sp) { Volume = volume };

                oneShot = new WaveOutEvent { DesiredLatency = 100 };
                oneShot.Init(withVolume);
                oneShot.Play();

                while (oneShot.PlaybackState == PlaybackState.Playing &&
                       sw.ElapsedMilliseconds < maxWaitMs)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NaAudioEngine] PlayOneShotAndWait error: {ex.Message}");
            }
            finally
            {
                try { oneShot?.Stop(); } catch { }
                try { oneShot?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
            }

            return sw.Elapsed;
        }
    }
}