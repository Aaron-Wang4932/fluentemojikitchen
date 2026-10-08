using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentEmojiKitchen.Presentation
{
    // One group/subgroup section of the grouped emoji grid — a large heading
    // (shown only on the first subgroup of a new group), a small subgroup
    // heading, and the emoji belonging to that subgroup.
    public class EmojiSection : INotifyPropertyChanged
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
        public bool ShowGroupHeading { get; }
        public ObservableCollection<EmojiItem> Emojis { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public EmojiSection(string groupName, string subgroupName, bool isFirstInGroup, IEnumerable<EmojiItem> emojis)
        {
            GroupName = groupName;
            _subgroupName = subgroupName;
            ShowGroupHeading = isFirstInGroup;
            Emojis = new ObservableCollection<EmojiItem>(emojis);
        }
        
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
