using System;

namespace GuessMelody.Core.Audio
{
    public interface IAudioEngine : IDisposable
    {
        // Фоновое воспроизведение трека
        void Load(string file);
        void Play();
        void Pause();
        void Stop();
        void Seek(TimeSpan position);

        TimeSpan PlayOneShotAndWait(string file, float volume = 1.0f, int maxWaitMs = 5000);

        bool IsPlaying { get; }
        TimeSpan Position { get; }
        TimeSpan Duration { get; }
        float Volume { get; set; }

        event EventHandler TrackEnded;

        // Короткий звук поверх (для тиков, right/wrong)
        void PlayOneShot(string file, float volume = 1.0f);
    }
}