using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace FluentEmojiKitchen.Services;

// Owns image retention only; metadata/index caches have their own refresh policy.
public sealed class KitchenImageCache : IDisposable
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private bool _disposed;
    private static readonly Regex OwnedName = new(@"^[0-9a-f]{64}\.png(?:\.[0-9a-f]{32}\.tmp)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public KitchenImageCache(string directory)
    {
        _directory = Path.GetFullPath(directory);
        // Sweep on a worker at startup, then hourly while the tray app is running.
        _timer = new Timer(_ => Sweep(DateTime.UtcNow), null, TimeSpan.Zero, TimeSpan.FromHours(1));
    }
    public IDisposable Acquire(string path)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!string.Equals(Path.GetDirectoryName(path), _directory, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Image must belong to this cache.", nameof(path));
            _leases.TryGetValue(path, out int count);
            _leases[path] = count + 1;
            Touch(path);
            return new Lease(this, path);
        }
    }
    private static void Touch(string path)
    {
        try { if (File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void Release(string path)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(path, out int count)) return;
            if (count == 1) _leases.Remove(path); else _leases[path] = count - 1;
            Touch(path);
        }
    }
    public void Sweep(DateTime utcNow)
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            foreach (string path in Directory.EnumerateFiles(_directory))
            {
                if (!OwnedName.IsMatch(Path.GetFileName(path))) continue;
                lock (_gate)
                {
                    if (_disposed) return;
                    string original = path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ? path[..^37] : path;
                    if (_leases.ContainsKey(original)) continue;
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < utcNow.AddDays(-7)) File.Delete(path);
                    }
                    catch (IOException) { } // A decoder or another app instance may have the file open.
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _timer.Dispose();
    }
    private sealed class Lease(KitchenImageCache owner, string path) : IDisposable
    {
        private KitchenImageCache? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(path);
    }
}
