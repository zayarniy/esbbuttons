using GuessMelody;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using GuessMelody.ViewModels;

namespace GuessMelody
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static ServiceProvider Services { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Лог-папка
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GuessMelody", "logs");
            Directory.CreateDirectory(logDir);

            Services = Bootstrapper.Build();

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    var log = Services?.GetService<Core.LogService>();
                    log?.Add(Core.Enums.LogKind.Error,
                        "DispatcherUnhandledException: " + args.Exception);
                }
                catch { }

                MessageBox.Show(args.Exception.Message, "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    var ex = args.ExceptionObject as Exception;
                    var log = Services?.GetService<Core.LogService>();
                    log?.Add(Core.Enums.LogKind.Error,
                        "UnhandledException: " + (ex?.ToString() ?? args.ExceptionObject?.ToString()));
                }
                catch { }
            };

            var main = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            MainWindow = main;
            main.Show();
            // Активировать мост heartbeat (создаётся лениво — принудительно резолвим)
            Services.GetRequiredService<GuessMelody.Services.HeartbeatBridge>();
            Services.GetRequiredService<GuessMelody.Services.PressBridge>();
            Services.GetRequiredService<GuessMelody.Services.AnswerWindowService>();
            Services.GetRequiredService<GuessMelody.Buttons.ButtonService>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Services?.GetService<Buttons.ButtonService>()?.Dispose(); } catch { }
            try { Services?.GetService<AnswerServer.AnswerHttpServer>(); } catch { }
            try { Services?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
