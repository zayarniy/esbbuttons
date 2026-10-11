using System;
using GuessMelody.Core.Models;

namespace GuessMelody.ViewModels
{
    public class TrackRowViewModel : ViewModelBase
    {
        public Track Track { get; }
        public string FileName => System.IO.Path.GetFileName(Track.RelativePath);
        public string RelativePath => Track.RelativePath;
        public double DurationSec => Track.DurationSec;

        public string DurationText =>
            Track.DurationSec > 0
                ? TimeSpan.FromSeconds(Track.DurationSec).ToString(@"mm\:ss")
                : "—";

        public string SizeText
        {
            get
            {
                var b = Track.FileSizeBytes;
                if (b <= 0) return "";
                if (b < 1024) return $"{b} B";
                if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
                return $"{b / (1024.0 * 1024.0):F1} MB";
            }
        }

        public bool IsUnsupported => Track.IsUnsupported;

        public string OverrideMark => Track.HasOverrides ? "★" : "";
        public string UnsupportedMark => Track.IsUnsupported ? "⚠" : "";

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set => Set(ref _isPlaying, value);
        }

        public TrackRowViewModel(Track t) { Track = t; }

        public void RefreshOverrides()
        {
            OnPropertyChanged(nameof(OverrideMark));
            OnPropertyChanged(nameof(DurationText));
        }
    }
}