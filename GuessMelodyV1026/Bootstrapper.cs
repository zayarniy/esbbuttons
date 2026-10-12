using GuessMelody.Audio;
using GuessMelody.Buttons;
using GuessMelody.Core;
using GuessMelody.Services;
using GuessMelody.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GuessMelody
{
    public static class Bootstrapper
    {
        public static ServiceProvider Build()
        {
            var services = new ServiceCollection();

            // -------- Core --------
            services.AddSingleton<LogService>();
            services.AddSingleton<GameEngine>();

            // -------- Services --------
            services.AddSingleton<DialogService>();
            services.AddSingleton<RecentFilesService>();
            services.AddSingleton<AudioCoordinator>();
            services.AddSingleton<ColorPickerService>();
            services.AddSingleton<SettingsPresetService>();
            services.AddSingleton<FolderScanner>();
            services.AddSingleton<FeedbackService>();

            // -------- Buttons --------
            services.AddSingleton<ButtonService>(sp =>
                new ButtonService(
                    sp.GetRequiredService<LogService>(),
                    sp.GetRequiredService<GameEngine>()));

            services.AddSingleton<PressBridge>();
            services.AddSingleton<HeartbeatBridge>();

            // -------- Audio --------
            services.AddSingleton<AudioEngine>();
            services.AddSingleton<PreviewEngine>();
            services.AddSingleton<BuzzerPlayer>();

            // -------- AnswerServer --------
            services.AddSingleton<AnswerWindowService>(sp =>
            {
                var wnd = new AnswerWindowService();
                var feedback = sp.GetRequiredService<FeedbackService>();
                feedback.ShowCorrectAnswer += (s, answer) => wnd.ShowCorrect(answer, 10);
                feedback.ShowWrongAnswer += (s, msg) => wnd.ShowWrong(msg, 3);
                return wnd;
            });

            // -------- ViewModels --------
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<GameTabViewModel>();
            services.AddSingleton<SettingsTabViewModel>();
            services.AddSingleton<ButtonsTabViewModel>();
            services.AddSingleton<FoldersTabViewModel>();
            services.AddSingleton<LogTabViewModel>();
            services.AddSingleton<AnswerServerTabViewModel>();

            services.AddTransient<PlayerWindowViewModel>();
            services.AddTransient<HostWindowViewModel>();

            return services.BuildServiceProvider();
        }
    }
}