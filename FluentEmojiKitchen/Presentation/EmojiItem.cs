using FluentEmojiKitchen.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentEmojiKitchen.Presentation
{
    public class EmojiItem : INotifyPropertyChanged
    {
        public EmojiModel Model { get; }
        public string Character => Model.Character;
        public string EmojiName => Model.Name;

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TileOpacity));
            }
        }
        public double TileOpacity => IsEnabled ? 1 : 0.25;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedOpacity));
            }
        }
        public double SelectedOpacity => IsSelected ? 1 : 0;

        public EmojiItem(EmojiModel model) => Model = model;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
