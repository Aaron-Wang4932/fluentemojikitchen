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
        private static readonly string RawEmojiFile = Path.Combine(AppDataFolder, "raw-emoji-file.txt");
        private static readonly string ParsedEmojiJSON = Path.Combine(AppDataFolder, "emoji-list.json");
        private static readonly string RawEmojiFileURL = "https://unicode.org/Public/emoji/latest/emoji-test.txt";
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

            string jsonContent = File.ReadAllText(ParsedEmojiJSON);
            var deserializedEmojis = JsonSerializer.Deserialize<List<EmojiModel>>(jsonContent) ?? new List<EmojiModel>();

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
                    string[] nameInfo = semicolonSplit[1].Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string emoji = string.Concat(codepoints.Select(char.ConvertFromUtf32));
                    string name = string.Join(" ", nameInfo, 3, nameInfo.Length - 3);

                    EmojiModel emojiModel = new EmojiModel(emoji, codepoints, name, curGroup, curSubgroup);
                    emojis.Add(emojiModel);
                } 
            }
            string jsonString = JsonSerializer.Serialize(emojis);
            File.WriteAllText(ParsedEmojiJSON, jsonString);
            //System.Diagnostics.Debug.WriteLine("finished json serializing");
        }
    }
}
