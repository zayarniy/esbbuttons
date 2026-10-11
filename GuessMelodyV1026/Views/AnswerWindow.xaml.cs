using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GuessMelody.Views
{
    public partial class AnswerWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;

        public AnswerWindow()
        {
            InitializeComponent();

            _closeTimer = new DispatcherTimer();
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };

            Loaded += (s, e) =>
            {
                // мягкое появление
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                BeginAnimation(OpacityProperty, fade);
            };
        }

        public void ShowAnswer(string title, string answer, TimeSpan duration, bool wrong = false)
        {
            TitleText.Text = title;
            AnswerText.Text = answer;

            // цвет рамки/заголовка: жёлтый для правильного, красный для неправильного
            var brush = wrong
                ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF4040")
                : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFD166");
            TitleText.Foreground = brush;

            if (Content is System.Windows.Controls.Border border)
                border.BorderBrush = brush;

            // Автозакрытие
            _closeTimer.Stop();
            _closeTimer.Interval = duration;
            _closeTimer.Start();
        }
    }
}