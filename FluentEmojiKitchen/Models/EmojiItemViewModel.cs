using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentEmojiKitchen.Models
{
    public class EmojiItemViewModel : INotifyPropertyChanged
    {
        public EmojiModel Model { get; }
        public string Character => Model.Character;
        public string EmojiName => Model.Name;

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

        public EmojiItemViewModel(EmojiModel model) => Model = model;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}