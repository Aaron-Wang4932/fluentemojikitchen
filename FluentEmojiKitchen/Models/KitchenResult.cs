using System;
namespace FluentEmojiKitchen.Models;

public sealed record KitchenResult(Uri? ImageUri, string? Message, int CopySize = 48, bool CanCopy = false);
