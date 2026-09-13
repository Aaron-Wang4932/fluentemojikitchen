using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace FluentEmojiKitchen.Models
{
    // One group/subgroup section of the grouped emoji grid — a large heading
    // (shown only on the first subgroup of a new group), a small subgroup
    // heading, and the emoji belonging to that subgroup.
    public class EmojiSectionViewModel
    {
        public string GroupName { get; }
        public string SubgroupName { get; }
        public Visibility GroupHeadingVisibility { get; }
        public List<EmojiItemViewModel> Emojis { get; }

        public EmojiSectionViewModel(string groupName, string subgroupName, bool isFirstInGroup, List<EmojiItemViewModel> emojis)
        {
            GroupName = groupName;
            SubgroupName = subgroupName;
            GroupHeadingVisibility = isFirstInGroup ? Visibility.Visible : Visibility.Collapsed;
            Emojis = emojis;
        }
    }
}