using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentEmojiKitchen.Models
{
    // One group/subgroup section of the grouped emoji grid — a large heading
    // (shown only on the first subgroup of a new group), a small subgroup
    // heading, and the emoji belonging to that subgroup.
    public class EmojiSectionViewModel : INotifyPropertyChanged
    {
        public string GroupName { get; }
        private string _subgroupName;
        public string SubgroupName 
        {
            get => _subgroupName;
            set
            {
                if(value == _subgroupName) return;
                _subgroupName = value;
                OnPropertyChanged();
            }
        }
        public Visibility GroupHeadingVisibility { get; }
        public ObservableCollection<EmojiItemViewModel> Emojis { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public EmojiSectionViewModel(string groupName, string subgroupName, bool isFirstInGroup, IEnumerable<EmojiItemViewModel> emojis)
        {
            GroupName = groupName;
            _subgroupName = subgroupName;
            GroupHeadingVisibility = isFirstInGroup ? Visibility.Visible : Visibility.Collapsed;
            Emojis = new ObservableCollection<EmojiItemViewModel>(emojis);
        }
        
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}