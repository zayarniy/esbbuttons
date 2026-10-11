using System;
using System.Windows;
using GuessMelody.Views;

namespace GuessMelody.Services
{
    public class AnswerWindowService
    {
        private AnswerWindow _current;

        public void ShowCorrect(string answer, int seconds = 10)
        {
            Show("✅ Правильный ответ", answer, seconds, wrong: false);
        }

        public void ShowWrong(string message = "❌ Неверный ответ", int seconds = 3)
        {
            Show("❌ Неверный ответ", message, seconds, wrong: true);
        }

        private void Show(string title, string text, int seconds, bool wrong)
        {
            // Если уже открыто — закрываем, чтобы не накладывалось
            try { _current?.Close(); } catch { }

            var win = new AnswerWindow();

            // Owner: PlayerWindow, если открыт, иначе HostWindow, иначе MainWindow
            Window owner = null;
            if (PlayerWindow.Current != null) owner = PlayerWindow.Current;
            else if (HostWindow.Current != null) owner = HostWindow.Current;
            else owner = Application.Current?.MainWindow;

            if (owner != null && owner.IsLoaded && !ReferenceEquals(owner, win))
                win.Owner = owner;

            _current = win;
            win.Closed += (s, e) => { if (ReferenceEquals(_current, win)) _current = null; };

            win.ShowAnswer(title, text, TimeSpan.FromSeconds(seconds), wrong);
            win.Show();
        }
    }
}