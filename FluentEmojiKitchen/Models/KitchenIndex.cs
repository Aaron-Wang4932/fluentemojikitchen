using System;
using System.Collections.Generic;
namespace FluentEmojiKitchen.Models;

// Serialized cache DTO. URLs are pooled once; pair entries store indices into Urls.
public sealed class KitchenIndex
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime SourceUtc { get; set; }
    public List<string> Supported { get; set; } = new();
    public Dictionary<string, string[]> Compatible { get; set; } = new();
    public List<string> Urls { get; set; } = new();
    public Dictionary<string, int[]> Latest { get; set; } = new();
}
