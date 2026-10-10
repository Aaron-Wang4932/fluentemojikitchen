using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FluentEmojiKitchen.Services.Windows;

// MRT resolves only our explicit immutable resource names to AppData files.
// This supports unpackaged WinUI without writing into the executable directory.
public static class EmojiFontResources
{
    private static readonly ConcurrentDictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<TextBlock, Registration> Targets = new();
    private sealed class Registration { }
    private static string? _currentPath;
    private static string? _currentKey;
    private static FontFamily? _current;
    public static IResourceManager CreateResourceManager()
    {
        var manager = new ResourceManager();
        manager.ResourceNotFound += (_, args) =>
        {
            string name = args.Name.Replace('\\', '/');
            int start = name.IndexOf("DynamicEmojiFonts/", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return;
            string key = name[start..];
            if (Paths.TryGetValue(key, out string? path))
                args.SetResolvedCandidate(new ResourceCandidate(ResourceCandidateKind.FilePath, path));
        };
        return manager;
    }
    public static bool IsInUse(string path) => Paths.Values.Contains(path);
    public static void Apply(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)) return;
        string key = "DynamicEmojiFonts/" + Path.GetFileName(path);
        string? previousKey = _currentKey;
        Paths[key] = path;
        _current = new FontFamily($"ms-appx:///{key}#Noto Color Emoji");
        _currentPath = path;
        _currentKey = key;
        Application.Current.Resources["NotoColorEmoji"] = _current;
        foreach (var target in Targets) target.Key.FontFamily = _current;
        // Keep the current and immediately preceding version during the UI transition.
        // Framework-native caches may outlive these managed objects, but this map is bounded.
        foreach (string oldKey in Paths.Keys)
            if (oldKey != key && oldKey != previousKey) Paths.TryRemove(oldKey, out _);
    }
    public static readonly DependencyProperty UseEmojiFontProperty = DependencyProperty.RegisterAttached(
        "UseEmojiFont", typeof(bool), typeof(EmojiFontResources), new PropertyMetadata(false, OnChanged));
    public static bool GetUseEmojiFont(DependencyObject target) => (bool)target.GetValue(UseEmojiFontProperty);
    public static void SetUseEmojiFont(DependencyObject target, bool value) => target.SetValue(UseEmojiFontProperty, value);
    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is TextBlock text && args.NewValue is true)
        {
            Targets.GetValue(text, _ => new Registration());
            if (_current is not null) text.FontFamily = _current;
        }
        else if (target is TextBlock removed) Targets.Remove(removed);
    }
}
