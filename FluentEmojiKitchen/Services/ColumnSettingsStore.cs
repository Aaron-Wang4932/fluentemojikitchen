using System;
using System.Globalization;
using System.IO;
using FluentEmojiKitchen.Configuration;
namespace FluentEmojiKitchen.Services;

// Preferences are user data, not disposable download caches.
public sealed class ColumnSettingsStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "settings-columns.txt");
    public int Load(int fallback)
    {
        try
        {
            if (File.Exists(_path) && int.TryParse(File.ReadAllText(_path), out int value) &&
                value >= AppOptions.MinimumColumns && value <= AppOptions.MaximumColumns) return value;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Math.Clamp(fallback, AppOptions.MinimumColumns, AppOptions.MaximumColumns);
    }
    public void Save(int columns)
    {
        if (columns < AppOptions.MinimumColumns || columns > AppOptions.MaximumColumns)
            throw new ArgumentOutOfRangeException(nameof(columns));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        CacheFiles.WriteText(_path, columns.ToString(CultureInfo.InvariantCulture));
    }
}
