using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using FluentEmojiKitchen.Helpers;


// To service and manage all internal and external data required by FluentEmojiKitchen.
namespace FluentEmojiKitchen.Services
{
    public class EmojiDataService : IEmojiCatalog
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluentEmojiKitchen");
        private static readonly string ParsedEmojiJSON = Path.Combine(AppDataFolder, "emoji-list.json");
        private static readonly string RawEmojiFile = Path.Combine(AppDataFolder, "raw.txt");
        private static readonly string RawEmojiFileURL = "https://unicode.org/Public/emoji/latest/emoji-test.txt";
        private static readonly string CLDRAnnotations = Path.Combine(AppDataFolder, "cldr-annot.json");
        private static readonly string CLDRAnnotationsURL = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-annotations-full/annotations/en/annotations.json";
        private static readonly string RecentEmojiFile = Path.Combine(AppDataFolder, "recent-emoji.json");
        private static readonly string SupportedEmojiAndKeywordsURL = "https://raw.githubusercontent.com/xsalazar/emoji-kitchen-backend/refs/heads/main/app/keywords.json";
        private static readonly string SupportedEmojiAndKeywordsFile = Path.Combine(AppDataFolder, "keyw-and-supported.json");

        private const int MaxRecentCount = 24;

        // A raw external source that gets folded into ParsedEmojiJSON.
        // NOTE TO SELF TO ADD A NEW SOURCE: add an entry and its merge step inside BuildParsedEmojiJSONAsync.
        private sealed record CachedSource(string Url, string LocalPath);

        private static readonly List<CachedSource> Sources = new()
        {
            // List of all emojis
            new CachedSource(RawEmojiFileURL, RawEmojiFile),
            // List of all emoji annotations
            new CachedSource(CLDRAnnotationsURL, CLDRAnnotations),
            // List of all supported emoji kitchen (fusion) emojis + some more search keywords
            new CachedSource(SupportedEmojiAndKeywordsURL, SupportedEmojiAndKeywordsFile)
        };

        public static bool NeedsRefresh() => CachePolicy.MonthlyExpired(ParsedEmojiJSON) || Sources.Any(s => CachePolicy.MonthlyExpired(s.LocalPath));

        public List<string> RecentCharacters { get; private set; } = [];
        IReadOnlyList<string> IEmojiCatalog.RecentCharacters => RecentCharacters;

        public IReadOnlyList<EmojiModel> Emojis { get; }

        public IReadOnlyList<EmojiModel> SupportedFusionEmojis { get; }

        private EmojiDataService(IReadOnlyList<EmojiModel> emojis, IReadOnlyList<EmojiModel> supportedFusionEmojis)
        {
            Emojis = emojis;
            SupportedFusionEmojis = supportedFusionEmojis;
        }

        // Factory constructor method.
        public static async Task<EmojiDataService> CreateAsync(IProgress<StartupProgress>? progress = null, CancellationToken cancellationToken = default, bool force = false)
        {
            Directory.CreateDirectory(AppDataFolder);

            // Refresh missing sources and sources older than one calendar month.
            bool anySourceRefreshed = false;
            for (int i = 0; i < Sources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Sources[i];
                double start = i * 20;
                progress?.Report(new($"Checking {Path.GetFileName(source.LocalPath)}...", start));
                if (await EnsureFreshAsync(source, p => progress?.Report(new(
                    $"Downloading {Path.GetFileName(source.LocalPath)}: {p.Description}", start + 20 * p.Fraction)), cancellationToken, force))
                    anySourceRefreshed = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Processing emoji names and keywords...", 65));


            // ParsedEmojiJSON already has everything (codepoints, names, groups, keywords) from the last time the app was run
            // Only rebuild when source changed or if JSON does not exist.
            if (force || CachePolicy.MonthlyExpired(ParsedEmojiJSON) || anySourceRefreshed)
                await BuildParsedEmojiJSONAsync();

            progress?.Report(new("Reading emoji catalogue...", 85));
            string jsonContent = File.ReadAllText(ParsedEmojiJSON);
            var deserializedEmojis = JsonSerializer.Deserialize<List<EmojiModel>>(jsonContent) ?? new List<EmojiModel>();
            var supportedFusionEmojis = LoadSupportedFusionEmojis(deserializedEmojis);

            var eds = new EmojiDataService(deserializedEmojis, supportedFusionEmojis);
            eds.LoadRecentEmoji();

            progress?.Report(new("Emoji catalogue ready", 100));
            return eds;
        }

        // Downloads the source if it's missing or stale. Returns whether it downloaded.
        private static async Task<bool> EnsureFreshAsync(CachedSource source, Action<DownloadProgress>? progress, CancellationToken token, bool force)
        {
            if (force || CachePolicy.MonthlyExpired(source.LocalPath))
            {
                try
                {
                    await DownloadHelper.DownloadFileAsync(source.Url, source.LocalPath, progress, token);
                    return true;
                }
                catch (Exception ex) when (!force && !token.IsCancellationRequested && File.Exists(source.LocalPath) &&
                    ex is System.Net.Http.HttpRequestException or IOException or OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"Using cached {source.LocalPath}: {ex.Message}");
                }
            }
            return false;
        }

        // Rebuilds ParsedEmojiJSON from scratch:
        // - parses the raw Unicode emoji-test.txt into EmojiModels
        // - merges in CLDR keyword annotations
        // - writes the fully-populated result to disk
        private static async Task BuildParsedEmojiJSONAsync()
        {
            var emojis = ParseUnicodeTxt();

            var annot = await LoadAnnotAsync();
            foreach (var emoji in emojis)
            {

                if (annot.TryGetValue(emoji.Character, out var keywords))
                {
                    if (emoji.Name == "cat with wry smile")
                        keywords.Add("smirk"); // because I like to use smirk-cat.

                    emoji.Keywords = keywords;
                }
            }

            string jsonString = JsonSerializer.Serialize(emojis);
            CacheFiles.WriteText(ParsedEmojiJSON, jsonString);
        }

        // Parses RawEmojiFile into EmojiModels (no keywords yet)
        private static List<EmojiModel> ParseUnicodeTxt()
        {
            string curGroup = "";
            string curSubgroup = "";
            string[] lines = File.ReadAllLines(RawEmojiFile);

            List<EmojiModel> emojis = new List<EmojiModel>();

            foreach (var line in lines)
            {
                if (line.StartsWith("# group:")) // populate group info
                    curGroup = line.Replace("# group: ", "").Trim();
                else if (line.StartsWith("# subgroup:")) // populate subgroup info
                    curSubgroup = line.Replace("# subgroup: ", "").Trim();

                // Actual emoji data within each group/subgroup; ignore the one annoying header line + ignore unqualified/minimally-qualified emoji.
                // Line will follow format: "code points; qualification # emoji name"
                else if (line.Contains("; fully-qualified") && !line.Contains("Format: "))
                {
                    string[] semicolonSplit = line.Split("; fully-qualified");
                    List<int> codepoints = semicolonSplit[0]
                        .Trim()
                        .Split(" ", StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.Parse(s, System.Globalization.NumberStyles.HexNumber))
                        .ToList();

                    // Skip adding all the country flags.
                    if (EmojiFilterHelper.IsCountryFlag(codepoints))
                        continue;

                    string[] nameInfo = semicolonSplit[1].Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string emoji = string.Concat(codepoints.Select(char.ConvertFromUtf32));
                    string name = string.Join(" ", nameInfo, 3, nameInfo.Length - 3);

                    EmojiModel emojiModel = new EmojiModel(emoji, codepoints, name, curGroup, curSubgroup);
                    emojis.Add(emojiModel);
                }
            }

            // Add regional indicators
            for (int i = 0; i < 26; i++)
            {
                int codepoint = 0x1F1E6 + i;
                char letter = (char)('A' + i);
                emojis.Add(new EmojiModel(
                    char.ConvertFromUtf32(codepoint),
                    new List<int> { codepoint },
                    $"Flag Selector {letter}",
                    "Flags",
                    "regional-indicator"));
            }

            return emojis;
        }

        private static async Task<Dictionary<string, List<string>>> LoadAnnotAsync()
        {
            using var mainDoc = JsonDocument.Parse(File.ReadAllText(CLDRAnnotations));
            var root = mainDoc.RootElement.GetProperty("annotations").GetProperty("annotations");

            var res = new Dictionary<string, List<string>>();
            foreach (var entry in root.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("default", out var keywords))
                {
                    res[entry.Name] = keywords.EnumerateArray()
                        .Select(k => k.GetString()!)
                        .ToList();
                }
            }

            using var addendumDoc = JsonDocument.Parse(File.ReadAllText(SupportedEmojiAndKeywordsFile));
            var addendumRoot = addendumDoc.RootElement.GetProperty("data");
            foreach (var entry in addendumRoot.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("keywords", out var keywords) && entry.Value.TryGetProperty("emoji", out var emojiChar))
                {
                    var keywordAddendum = keywords.EnumerateArray()
                        .Select(k => k.GetString()!)
                        .ToList();
                    var key = emojiChar.GetString()!;

                    res[key] = res.TryGetValue(key, out var existingKeywords)
                        ? existingKeywords.Concat(keywordAddendum).Distinct().ToList()
                        : keywordAddendum;
                }
            }


            return res;
        }


        private static List<EmojiModel> LoadSupportedFusionEmojis(IReadOnlyList<EmojiModel> allEmojis)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SupportedEmojiAndKeywordsFile));
            var knownSupportedCodepoints = doc.RootElement.GetProperty("knownSupportedEmoji")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToHashSet();

            return allEmojis
                .Where(e => knownSupportedCodepoints.Contains(string.Join("-", e.Codepoints.Select(cp => cp.ToString("x")))))
                .ToList();
        }


        private void LoadRecentEmoji()
        {
            if (!File.Exists(RecentEmojiFile))
            {
                RecentCharacters = new();
                File.Create(RecentEmojiFile).Dispose();
                return;
            }
            try
            {
                RecentCharacters = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentEmojiFile)) ?? [];
            }
            catch
            {
                RecentCharacters = [];
            }
        }
        public void AddRecent(string character)
        {
            if (RecentCharacters.Contains(character))
                return;
            RecentCharacters.Insert(0, character); // most recent goes to the front

            if (RecentCharacters.Count > MaxRecentCount)
                RecentCharacters = RecentCharacters.Take(MaxRecentCount).ToList();

            File.WriteAllText(RecentEmojiFile, JsonSerializer.Serialize(RecentCharacters));
        }
    }
}
