using System;
using System.Collections.Generic;
using System.Linq;
using FluentEmojiKitchen.Helpers;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Services;

public sealed class EmojiQueryService
{
    public IEnumerable<EmojiModel> VisibleEmojis(IEnumerable<EmojiModel> emojis) =>
        emojis.Where(e => !EmojiFilterHelper.IsSkinToneVariant(e));

    public List<EmojiItem> Search(IEnumerable<EmojiModel> emojis, string query) =>
        emojis.Where(e => Matches(e, query.Trim())).Select(e => new EmojiItem(e)).ToList();

    private static bool Matches(EmojiModel emoji, string query) =>
        emoji.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        emoji.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        emoji.Subgroup.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (emoji.Keywords?.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false);

    public List<EmojiSection> Group(IEnumerable<EmojiModel> emojis, bool includeSubgroups)
    {
        if (!includeSubgroups)
            return emojis.GroupBy(e => e.Group)
                .Select(g => new EmojiSection(g.Key, "", true, g.Select(e => new EmojiItem(e))))
                .ToList();

        var sections = new List<EmojiSection>();
        string? lastGroup = null;
        foreach (var group in emojis.GroupBy(e => (e.Group, e.Subgroup)))
        {
            sections.Add(new EmojiSection(group.Key.Group, group.Key.Subgroup,
                lastGroup != group.Key.Group, group.Select(e => new EmojiItem(e))));
            lastGroup = group.Key.Group;
        }
        return sections;
    }
}
