namespace GuessMelody.Core.Enums
{
    public enum RoundState
    {
        Idle,
        Countdown,
        Playing,
        WaitingForAnswer,
        Scored,
        Finished
    }

    public enum LogKind
    {
        Raw, Connect, Reconnect, Disconnect, Press, Hb,
        System, Error, Game, Audio, Score, Http
    }
}