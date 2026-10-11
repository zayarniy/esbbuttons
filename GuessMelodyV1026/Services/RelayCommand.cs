using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Input;

namespace GuessMelody.Services
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) =>
            _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class DialogService
    {
        public string OpenFile(string filter, string title = "Открыть файл")
        {
            var dlg = new OpenFileDialog { Filter = filter, Title = title };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string SaveFile(string filter, string defaultName = "", string title = "Сохранить файл")
        {
            var dlg = new SaveFileDialog { Filter = filter, Title = title, FileName = defaultName };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string BrowseFolder(string title = "Выберите папку")
        {
            // WPF не имеет своего folder browser. Используем WinForms через интероп.
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = true;
                var result = dlg.ShowDialog();
                return result == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        public void Info(string message, string title = "Информация") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void Warn(string message, string title = "Внимание") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void Error(string message, string title = "Ошибка") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool Confirm(string message, string title = "Подтверждение") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;
    }
}