using System.Collections.ObjectModel;
using GuessMelody.Core.Models;

namespace GuessMelody.ViewModels
{
    public class CategoryRowViewModel : ViewModelBase
    {
        public Category Category { get; }
        public string Name => Category.Name;
        public int Count => Category.Tracks?.Count ?? 0;
        public string CountText => $"({Count})";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        public CategoryRowViewModel(Category c)
        {
            Category = c;
        }

        public void RefreshCount()
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
        }
    }
}