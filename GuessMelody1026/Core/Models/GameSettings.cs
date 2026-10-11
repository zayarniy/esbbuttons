using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace GuessMelody.Core.Models
{
    public class GameSettings : INotifyPropertyChanged
    {
        // ---------- Полуавтоматические поля ----------
        // (все публичные — сериализуются. INotifyPropertyChanged — для UI)
        // Реализуем через backing fields.

        private int _playDurationSec = 30;
        public int PlayDurationSec
        {
            get => _playDurationSec;
            set { _playDurationSec = value; OnPropertyChanged(); }
        }

        public StartAtMode _startAtMode = StartAtMode.FromRandomPlace;
        public StartAtMode StartAtMode
        {
            get => _startAtMode;
            set { _startAtMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSpecificStart)); }
        }

        private double _startAtSec = 15.0;
        public double StartAtSec
        {
            get => _startAtSec;
            set { _startAtSec = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsSpecificStart => StartAtMode == StartAtMode.FromSpecificTime;

        // ---------- Звук старта раунда ----------
        private bool _playRoundStartSound = true;
        public bool PlayRoundStartSound
        {
            get => _playRoundStartSound;
            set { _playRoundStartSound = value; OnPropertyChanged(); }
        }

        private string _roundStartSoundFile = "sounds/round_start.wav";
        public string RoundStartSoundFile
        {
            get => _roundStartSoundFile;
            set { _roundStartSoundFile = value; OnPropertyChanged(); }
        }

        // ---------- Обратный отсчёт перед раундом ----------
        private bool _playCountdown = true;
        public bool PlayCountdown
        {
            get => _playCountdown;
            set { _playCountdown = value; OnPropertyChanged(); }
        }

        private int _countdownSeconds = 3;
        public int CountdownSeconds
        {
            get => _countdownSeconds;
            set { _countdownSeconds = value; OnPropertyChanged(); }
        }

        private string _countdownTickSoundFile = "sounds/tick.wav";
        public string CountdownTickSoundFile
        {
            get => _countdownTickSoundFile;
            set { _countdownTickSoundFile = value; OnPropertyChanged(); }
        }

        private string _countdownEndSoundFile = "sounds/go.wav";
        public string CountdownEndSoundFile
        {
            get => _countdownEndSoundFile;
            set { _countdownEndSoundFile = value; OnPropertyChanged(); }
        }

        // ---------- Обратный отсчёт на ответ ----------
        private int _answerSeconds = 10;
        public int AnswerSeconds
        {
            get => _answerSeconds;
            set { _answerSeconds = value; OnPropertyChanged(); }
        }

        private string _answerTickSoundFile = "sounds/tick.wav";
        public string AnswerTickSoundFile
        {
            get => _answerTickSoundFile;
            set { _answerTickSoundFile = value; OnPropertyChanged(); }
        }

        // ---------- Звуки результата ----------
        private string _rightSoundFile = "sounds/right.wav";
        public string RightSoundFile
        {
            get => _rightSoundFile;
            set { _rightSoundFile = value; OnPropertyChanged(); }
        }

        private string _wrongSoundFile = "sounds/wrong.wav";
        public string WrongSoundFile
        {
            get => _wrongSoundFile;
            set { _wrongSoundFile = value; OnPropertyChanged(); }
        }

        // ---------- Раунд ----------
        private bool _autoNextRound = false;
        public bool AutoNextRound
        {
            get => _autoNextRound;
            set { _autoNextRound = value; OnPropertyChanged(); }
        }

        private int _maxRounds = 0;
        public int MaxRounds
        {
            get => _maxRounds;
            set { _maxRounds = value; OnPropertyChanged(); }
        }

        // ---------- Сетка экрана ----------
        private int _gridColumns = 3;
        public int GridColumns
        {
            get => _gridColumns;
            set { _gridColumns = value; OnPropertyChanged(); }
        }

        private int _gridRows = 3;
        public int GridRows
        {
            get => _gridRows;
            set { _gridRows = value; OnPropertyChanged(); }
        }

        public int SchemaVersion { get; set; } = 1;

        // ---------- INotifyPropertyChanged ----------
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}