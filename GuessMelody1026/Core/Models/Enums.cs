using System;

namespace GuessMelody.Core.Models
{
    public enum RoundState
    {
        Idle,
        RoundStart,
        Countdown,
        TrackPlaying,
        WaitingAnswer,
        ScoreApplied,
        NoOneAnswered
    }

    public enum StartAtMode
    {
        FromBeginning,
        FromRandomPlace,
        FromSpecificTime
    }

    public enum EndReason
    {
        Scored,          // ведущий нажал «Да»
        AllAnsweredWrong,// все игроки ответили «Нет», раунд закрыт
        NoOne,           // никто не нажал за PlayDurationSec
        NoTracks,        // в категории не осталось треков
        Manual           // ведущий принудительно закрыл
    }
}