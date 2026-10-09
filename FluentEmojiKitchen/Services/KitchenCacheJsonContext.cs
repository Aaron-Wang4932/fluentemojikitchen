using System.Text.Json.Serialization;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

// Explicit metadata keeps the new cache serializer compatible with trimmed publishing.
[JsonSerializable(typeof(KitchenIndex))]
internal partial class KitchenCacheJsonContext : JsonSerializerContext { }
