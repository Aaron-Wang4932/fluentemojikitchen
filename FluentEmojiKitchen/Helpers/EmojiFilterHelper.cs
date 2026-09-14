using FluentEmojiKitchen.Models;
using System.Collections.Generic;
using System.Linq;

namespace FluentEmojiKitchen.Helpers
{
    public class EmojiFilterHelper
    {
        private EmojiFilterHelper() { }
        public static bool IsCountryFlag(List<int> codepoints) => codepoints.Count == 2 && codepoints.All(cp => cp is >= 0x1F1E6 and <= 0x1F1FF);

        // Fitzpatrick skin-tone modifiers occupy this exact codepoint range.
        // Any emoji containing one is a toned variant of some other base entry.
        public static bool IsSkinToneVariant(EmojiModel emoji) => emoji.Codepoints.Any(cp => cp is >= 0x1F3FB and <= 0x1F3FF);
        
    }
}
