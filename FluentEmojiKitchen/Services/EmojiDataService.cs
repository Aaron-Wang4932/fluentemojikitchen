using FluentEmojiKitchen.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentEmojiKitchen.Services
{
    public class EmojiDataService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluentEmojiKitchen");
        private static readonly string RawEmojiFile = Path.Combine(AppDataFolder, "raw.txt");
        private static readonly string RawEmojiFileURL = "https://unicode.org/Public/emoji/latest/emoji-test.txt";
        private static readonly string ParsedEmojiJSON = Path.Combine(AppDataFolder, "emoji-list.json");
        private static readonly string CLDRAnnotations = Path.Combine(AppDataFolder, "cldr-annot.json");
        private static readonly string CLDRAnnotationsURL = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-annotations-full/annotations/en/annotations.json";


        public IReadOnlyList<EmojiModel> Emojis { get; }

        private EmojiDataService(IReadOnlyList<EmojiModel> emojis)
        {
            Emojis = emojis;
        }

        public static async Task<EmojiDataService> CreateAsync()
        {
            Directory.CreateDirectory(AppDataFolder);

            // Recache RawEmojiFile if it does not exist or if the file age > 1 week = 7 days.
            // Recache ParsedEmojiJSON if it does not exist or if RawEmojiFile was recached.
            bool recachedRawFile = false;
            if (!File.Exists(RawEmojiFile) || (DateTime.Now - File.GetCreationTime(RawEmojiFile)).TotalDays > 7)
            {
                //System.Diagnostics.Debug.WriteLine("getting raw data");
                await DownloadFileAsync(RawEmojiFileURL, RawEmojiFile);
                recachedRawFile = true;
            }

            if(!File.Exists(ParsedEmojiJSON) || recachedRawFile)
                ParseToJSONFromTxt();

            if (!File.Exists(CLDRAnnotations) || recachedRawFile)
                await DownloadFileAsync(CLDRAnnotationsURL, CLDRAnnotations);

            // Deserialize JSON content to list of all valid emojis.
            string jsonContent = File.ReadAllText(ParsedEmojiJSON);
            var deserializedEmojis = JsonSerializer.Deserialize<List<EmojiModel>>(jsonContent) ?? new List<EmojiModel>();

            // Add search keywords to EmojiModel.
            var annot = await LoadAnnotAsync();
            foreach (var emoji in deserializedEmojis)
            {
                if (annot.TryGetValue(emoji.Character, out var keywords))
                {
                    if (emoji.Name == "cat with wry smile")
                    {
                        keywords.Add("smirk");
                        emoji.Keywords = keywords;
                    } else emoji.Keywords = keywords;

                }
            }

            return new EmojiDataService(deserializedEmojis);
        }
        private static async Task DownloadFileAsync(string url, string downloadPath)
        {
            var handler = new HttpClientHandler { UseProxy = false };
            using HttpClient client = new HttpClient(handler);
            using Stream httpStream = await client.GetStreamAsync(url);
            using FileStream fileStream = File.Create(downloadPath);
            await httpStream.CopyToAsync(fileStream);
        }

        private static void ParseToJSONFromTxt()
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
                    if (IsCountryFlag(codepoints))
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

            string jsonString = JsonSerializer.Serialize(emojis);
            File.WriteAllText(ParsedEmojiJSON, jsonString);
            //System.Diagnostics.Debug.WriteLine("finished json serializing");
        }

        private static bool IsCountryFlag(List<int> codepoints) =>
            codepoints.Count == 2 && codepoints.All(cp => cp is >= 0x1F1E6 and <= 0x1F1FF);

        private static async Task<Dictionary<string, List<string>>> LoadAnnotAsync()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CLDRAnnotations)); 
            var root = doc.RootElement.GetProperty("annotations").GetProperty("annotations");

            var res = new Dictionary<string, List<string>>();
            foreach (var entry in root.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("default", out var keywordsEl))
                {
                    res[entry.Name] = keywordsEl.EnumerateArray()
                        .Select(k => k.GetString()!)
                        .ToList();
                }
            }
            return res;
        }
    }
}
