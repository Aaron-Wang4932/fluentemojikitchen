using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Helpers;
using FluentEmojiKitchen.Models;
namespace FluentEmojiKitchen.Services;

// Immutable generations avoid replacing a font that DirectWrite may still hold open.
public sealed class EmojiFontCache
{
    private readonly Func<string, string, Action<DownloadProgress>?, CancellationToken, Task> _download;
    public EmojiFontCache(string directory,
        Func<string, string, Action<DownloadProgress>?, CancellationToken, Task>? download = null)
    {
        _directory = Path.Combine(directory, "fonts");
        _download = download ?? DownloadHelper.DownloadFileAsync;
    }
    private const string Source = "https://raw.githubusercontent.com/googlefonts/noto-emoji/main/2D/fonts/NotoColorEmoji_WindowsCompatible.ttf";
    private readonly string _directory;
    public bool NeedsRefresh()
    {
        string pointer = Path.Combine(_directory, "current-font.txt");
        if (!File.Exists(pointer)) return true;
        string name = File.ReadAllText(pointer).Trim();
        return name != Path.GetFileName(name) || !File.Exists(Path.Combine(_directory, name)) || CachePolicy.MonthlyExpired(pointer);
    }
    public async Task<string> LoadAsync(IProgress<StartupProgress>? progress, CancellationToken token, bool force = false)
    {
        Directory.CreateDirectory(_directory);
        string pointer = Path.Combine(_directory, "current-font.txt");
        string? current = null;
        if (File.Exists(pointer))
        {
            string name = File.ReadAllText(pointer).Trim();
            if (name == Path.GetFileName(name) && name.StartsWith("NotoColorEmoji-", StringComparison.Ordinal) && name.EndsWith(".ttf", StringComparison.Ordinal))
            {
                string path = Path.Combine(_directory, name);
                if (ValidFont(path)) current = path;
            }
        }
        if (!force && current is not null && !CachePolicy.MonthlyExpired(pointer)) return current;
        string candidate = Path.Combine(_directory, $"NotoColorEmoji-{Guid.NewGuid():N}.ttf");
        try
        {
            progress?.Report(new("Downloading emoji font...", 0));
            await _download(Source, candidate,
                p => progress?.Report(new($"Downloading emoji font: {p.Description}", p.Fraction * 95)), token).ConfigureAwait(false);
            if (!ValidFont(candidate)) throw new InvalidDataException("The downloaded emoji font is incomplete or invalid.");
            token.ThrowIfCancellationRequested();
            // Download on every force request, but load only distinct bytes into DirectWrite.
            // Identical refreshes must not produce new font identities/glyph caches.
            string digest;
            using (var stream = File.OpenRead(candidate)) digest = Convert.ToHexString(SHA256.HashData(stream));
            string promoted = Path.Combine(_directory, $"NotoColorEmoji-{digest}.ttf");
            bool same = false;
            if (ValidFont(promoted))
            {
                using var existing = File.OpenRead(promoted);
                same = Convert.ToHexString(SHA256.HashData(existing)) == digest;
            }
            if (!same) File.Move(candidate, promoted, true);
            CacheFiles.WriteText(pointer, Path.GetFileName(promoted)); // Refresh receipt, not loaded file.
            progress?.Report(new("Emoji font ready", 100));
            return promoted;
        }
        catch (Exception ex) when (!force && current is not null && !token.IsCancellationRequested &&
            ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException)
        {
            progress?.Report(new("Font update unavailable — using saved font", 100));
            return current;
        }
        finally { CacheFiles.DeleteTemporary(candidate); }
    }
    public static bool ValidFont(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            if (file.Read(header) != 12 || BinaryPrimitives.ReadUInt32BigEndian(header) != 0x00010000) return false;
            int count = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
            if (count < 4 || count > 256 || file.Length < 12 + count * 16) return false;
            bool names = false, cmap = false, color = false;
            Span<byte> table = stackalloc byte[16];
            for (int i = 0; i < count; i++)
            {
                if (file.Read(table) != 16) return false;
                uint tag = BinaryPrimitives.ReadUInt32BigEndian(table);
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(table[8..]);
                uint length = BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
                if ((ulong)offset + length > (ulong)file.Length) return false;
                names |= tag == 0x6e616d65; cmap |= tag == 0x636d6170;
                color |= tag is 0x43424454 or 0x434f4c52 or 0x53564720;
            }
            return names && cmap && color;
        }
        catch (IOException) { return false; }
    }
    // Called before a font is installed in the current process, preserving any live generations.
    public void PruneOldGenerations(string current, Func<string, bool> inUse)
    {
        foreach (string path in Directory.EnumerateFiles(_directory, "NotoColorEmoji-*.ttf"))
            if (path != current && !inUse(path)) CacheFiles.DeleteTemporary(path);
    }
}
