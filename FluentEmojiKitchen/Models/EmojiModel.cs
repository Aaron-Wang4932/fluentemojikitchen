using System.Collections.Generic;
using System.Text;

namespace FluentEmojiKitchen.Models
{
    public record EmojiModel(
        string Character,
        IReadOnlyList<int> Codepoints,
        string Name,
        string Group,
        string Subgroup
    )
    {
        protected virtual bool PrintMembers(StringBuilder builder)
        {
            builder.Append($"Character = {Character}, ");

            // Format the list gracefully, handling potential nulls safely
            string codepointsText = Codepoints is not null
                ? $"[{string.Join(", ", Codepoints)}]"
                : "null";
            builder.Append($"Codepoints = {codepointsText}, ");

            builder.Append($"Name = {Name}, ");
            builder.Append($"Group = {Group}, ");
            builder.Append($"Subgroup = {Subgroup}");

            return true;
        }
    }
}