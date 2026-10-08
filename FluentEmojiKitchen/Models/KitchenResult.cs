using System;
namespace FluentEmojiKitchen.Models;

// The future fusion service can supply an image URI when the real lookup is implemented.
public sealed record KitchenResult(Uri? ImageUri, string? Message);
