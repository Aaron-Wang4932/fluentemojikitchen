using System;
using FluentEmojiKitchen.Views;
namespace FluentEmojiKitchen.Listeners;

// Owns external subscriptions; views keep their local visual event handlers.
public sealed class ApplicationInputListener : IDisposable
{
    private readonly GlobalHotkeyListener _hotkey = new();
    private readonly TrayIconView _tray = new();
    private readonly Action _toggle;
    private readonly Action _exit;
    private bool _disposed;
    public ApplicationInputListener(Action toggle, Action exit)
    {
        _toggle = toggle;
        _exit = exit;
        _hotkey.HotkeyPressed += _toggle;
        _tray.ToggleRequested += _toggle;
        _tray.ExitRequested += _exit;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }
    public void Start() => _hotkey.Install();
    private void OnProcessExit(object? sender, EventArgs e) => _hotkey.Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _hotkey.HotkeyPressed -= _toggle;
        _tray.ToggleRequested -= _toggle;
        _tray.ExitRequested -= _exit;
        _hotkey.Dispose();
        _tray.Dispose();
    }
}
