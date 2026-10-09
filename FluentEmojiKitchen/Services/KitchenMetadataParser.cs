using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

// Reads UTF-8 incrementally. Never builds a DOM/string for the full metadata file.
public sealed class KitchenMetadataParser
{
    public KitchenIndex Parse(string path, Action<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var file = File.OpenRead(path);
        var index = new KitchenIndex { SourceUtc = File.GetLastWriteTimeUtc(path) };
        var supported = new HashSet<string>(StringComparer.Ordinal);
        var partners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pairs = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var urls = new Dictionary<string, int>(StringComparer.Ordinal);
        string rootProperty = "", sectionProperty = "", recordProperty = "", first = "", second = "";
        string? url = null;
        bool latest = false, sawData = false;
        byte[] buffer = new byte[65536];
        int retained = 0;
        long consumed = 0, lastReport = 0;
        var state = new JsonReaderState();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = file.Read(buffer, retained, buffer.Length - retained);
            bool final = count == 0;
            int length = retained + count;
            var reader = new Utf8JsonReader(buffer.AsSpan(0, length), final, state);
            while (reader.Read())
            {
                int depth = reader.CurrentDepth;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (depth == 1) rootProperty = reader.GetString()!;
                    else if (rootProperty == "data")
                    {
                        if (depth == 2) first = reader.GetString()!;
                        else if (depth == 3) sectionProperty = reader.GetString()!;
                        else if (depth == 4 && sectionProperty == "combinations") second = reader.GetString()!;
                        else if (depth == 6 && sectionProperty == "combinations") recordProperty = reader.GetString()!;
                    }
                }
                if (rootProperty == "knownSupportedEmoji" && depth == 2 && reader.TokenType == JsonTokenType.String)
                    supported.Add(reader.GetString()!);
                if (rootProperty != "data") continue;
                if (depth == 1 && reader.TokenType == JsonTokenType.StartObject) sawData = true;
                if (sectionProperty != "combinations") continue;
                if (depth == 5 && reader.TokenType == JsonTokenType.StartObject)
                {
                    url = null; latest = false; recordProperty = "";
                }
                else if (depth == 6 && reader.TokenType == JsonTokenType.String && recordProperty == "gStaticUrl")
                    url = reader.GetString();
                else if (depth == 6 && recordProperty == "isLatest" && reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                    latest = reader.GetBoolean();
                else if (depth == 5 && reader.TokenType == JsonTokenType.EndObject)
                {
                    if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) throw new InvalidDataException("Missing emoji pair.");
                    AddPartner(partners, first, second);
                    AddPartner(partners, second, first);
                    if (latest)
                    {
                        if (!IsImageUrl(url)) throw new InvalidDataException("Invalid latest image URL.");
                        if (!urls.TryGetValue(url!, out int id))
                        {
                            id = index.Urls.Count; urls.Add(url!, id); index.Urls.Add(url!);
                        }
                        string pair = PairKey(first, second);
                        if (!pairs.TryGetValue(pair, out var values)) pairs.Add(pair, values = new());
                        values.Add(id);
                    }
                }
            }
            int used = (int)reader.BytesConsumed;
            state = reader.CurrentState;
            consumed += used;
            retained = length - used;
            buffer.AsSpan(used, retained).CopyTo(buffer);
            if (consumed - lastReport >= 262144 || final)
            {
                progress?.Invoke(file.Length == 0 ? 1 : (double)consumed / file.Length);
                lastReport = consumed;
            }
            if (final) break;
            if (retained == buffer.Length)
            {
                if (buffer.Length >= 4 * 1024 * 1024) throw new InvalidDataException("Unexpectedly large JSON token.");
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }
        if (!sawData || supported.Count == 0 || partners.Count == 0 || index.Urls.Count == 0)
            throw new InvalidDataException("Kitchen metadata contains no usable combinations.");
        // Include endpoints even if the upstream support list lags its combinations.
        supported.UnionWith(partners.Keys);
        index.Supported = supported.OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (string key in index.Supported)
            index.Compatible[key] = partners.TryGetValue(key, out var set)
                ? set.OrderBy(x => x, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        foreach (var pair in pairs)
            index.Latest[pair.Key] = pair.Value.OrderBy(id => index.Urls[id], StringComparer.Ordinal).ToArray();
        return index;
    }
    private static void AddPartner(Dictionary<string, HashSet<string>> map, string a, string b)
    {
        if (!map.TryGetValue(a, out var set)) map.Add(a, set = new(StringComparer.Ordinal));
        set.Add(b);
    }
    public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
    public static bool IsImageUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "www.gstatic.com" &&
        uri.AbsolutePath.StartsWith("/android/keyboard/emojikitchen/", StringComparison.Ordinal);
}
