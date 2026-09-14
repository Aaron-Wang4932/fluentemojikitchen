using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace FluentEmojiKitchen.Helpers
{
    public class DownloadHelper
    {
        private DownloadHelper() { }
        public static async Task DownloadFileAsync(string url, string downloadPath)
        {
            var handler = new HttpClientHandler { UseProxy = false };
            using HttpClient client = new HttpClient(handler);
            using Stream httpStream = await client.GetStreamAsync(url);
            using FileStream fileStream = File.Create(downloadPath);
            await httpStream.CopyToAsync(fileStream);
        }
    }
}
