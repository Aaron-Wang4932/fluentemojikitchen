using System;
using System.IO;
namespace FluentEmojiKitchen.Services;

internal static class CacheFiles
{
    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentEmojiKitchen");
    internal static string Temporary(string destination) => destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
    internal static void DeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    internal static void WriteText(string destination, string contents)
    {
        string temporary = Temporary(destination);
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, destination, true);
        }
        finally { DeleteTemporary(temporary); }
    }
}
