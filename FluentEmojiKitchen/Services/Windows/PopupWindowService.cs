using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Interop;
using FluentEmojiKitchen.Listeners;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
namespace FluentEmojiKitchen.Services.Windows;

public sealed class PopupWindowService : IPopupWindow, IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _handle;
    private readonly OutsideClickListener _outsideClicks;
    private readonly PopupNativeMethods.SubclassProc _subclass;
    private readonly PopupNativeMethods.WinEventProc _foregroundCallback;
    private static readonly UIntPtr SubclassId = new(0x454B02);
    private IntPtr _foregroundHook, _target;
    private uint _targetProcess, _targetThread;
    private int _session;
    private bool _visible, _searchMode, _disposed;
    public event Action? Hidden;
    internal int CurrentSession => _visible ? _session : 0;

    public PopupWindowService(Window window)
    {
        // Configure popup settings.
        _window = window;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false; presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false); presenter.IsAlwaysOnTop = true;
        }
        appWindow.IsShownInSwitchers = false;
        SetNonActivating(true);
        _subclass = OnWindowMessage;
        if (!PopupNativeMethods.SetWindowSubclass(_handle, _subclass, SubclassId, UIntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { _outsideClicks = new OutsideClickListener(_handle, window.DispatcherQueue); }
        catch
        {
            PopupNativeMethods.RemoveWindowSubclass(_handle, _subclass, SubclassId);
            throw;
        }
        _outsideClicks.OutsideClicked += OnOutsideClicked;
        _foregroundCallback = (_, _, hwnd, _, _, _, _) =>
            window.DispatcherQueue.TryEnqueue(() => OnForegroundChanged(hwnd));
        _foregroundHook = PopupNativeMethods.SetWinEventHook(3, 3, IntPtr.Zero, _foregroundCallback, 0, 0, 0);
        if (_foregroundHook == IntPtr.Zero)
        {
            _outsideClicks.OutsideClicked -= OnOutsideClicked;
            _outsideClicks.Dispose();
            PopupNativeMethods.RemoveWindowSubclass(_handle, _subclass, SubclassId);
            throw new InvalidOperationException("Could not monitor popup focus changes.");
        }
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/Icons/AppIcon.ico");
        if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        appWindow.Resize(new global::Windows.Graphics.SizeInt32(360, (int)(display.WorkArea.Height * 0.75)));
        appWindow.Closing += OnClosing;
    }
    private void SetNonActivating(bool enabled)
    {
        int style = WindowNativeMethods.GetWindowLong(_handle, WindowNativeMethods.GwlExStyle);
        style = (style | WindowNativeMethods.WsExToolWindow) & ~WindowNativeMethods.WsExAppWindow;
        style = enabled ? style | PopupNativeMethods.WsExNoActivate : style & ~PopupNativeMethods.WsExNoActivate;
        WindowNativeMethods.SetWindowLong(_handle, WindowNativeMethods.GwlExStyle, style);
    }
    private IntPtr OnWindowMessage(IntPtr hwnd, uint message, UIntPtr wp, IntPtr lp, UIntPtr id, UIntPtr data)
    {
        if (!_searchMode && (message == PopupNativeMethods.WmMouseActivate || message == PopupNativeMethods.WmPointerActivate))
            return new IntPtr(3); // MA_NOACTIVATE / PA_NOACTIVATE: keep the input event.
        return PopupNativeMethods.DefSubclassProc(hwnd, message, wp, lp);
    }
    public void Toggle()
    {
        if (_disposed) return;
        if (_visible) Hide(); else Show();
    }
    private void Show()
    {
        var foreground = WindowNativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || PopupNativeMethods.BelongsToPopup(foreground, _handle)) return;
        _target = foreground;
        _targetThread = PopupNativeMethods.GetWindowThreadProcessId(_target, out _targetProcess);
        unchecked { ++_session; if (_session == 0) ++_session; }
        _searchMode = false; SetNonActivating(true);
        _visible = true;
        _window.AppWindow.Show(false);
        _outsideClicks.Arm(_session);
    }
    public void BeginSearch(Action focusSearch)
    {
        if (!SessionIsOpen(_session)) return;
        _searchMode = true; SetNonActivating(false);
        if (WindowNativeMethods.SetForegroundWindow(_handle)) focusSearch();
        else { _searchMode = false; SetNonActivating(true); }
    }
    private bool SessionIsOpen(int session) => !_disposed && _visible && session == _session && _outsideClicks.IsArmed(session);
    private bool TargetExists()
    {
        if (_target == IntPtr.Zero || !PopupNativeMethods.IsWindow(_target)) return false;
        uint thread = PopupNativeMethods.GetWindowThreadProcessId(_target, out uint process);
        return thread == _targetThread && process == _targetProcess;
    }
    internal bool CanInsert(int session) => SessionIsOpen(session) && TargetExists() &&
        WindowNativeMethods.GetForegroundWindow() == _target;
    internal async Task<bool> PrepareInsertionAsync(int session)
    {
        if (!SessionIsOpen(session) || !TargetExists()) return false;
        if (WindowNativeMethods.GetForegroundWindow() == _target) return true;
        if (!_searchMode || !PopupNativeMethods.BelongsToPopup(WindowNativeMethods.GetForegroundWindow(), _handle)) return false;
        _searchMode = false; SetNonActivating(true);
        if (!WindowNativeMethods.SetForegroundWindow(_target)) return false;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (!SessionIsOpen(session)) return false;
            if (CanInsert(session)) return true;
            await Task.Delay(10);
        }
        return false;
    }
    private void OnOutsideClicked(int session)
    {
        // Never restore the old target here; the outside click may select another app.
        if (!_disposed && _visible && session == _session) HideCore(false);
    }
    private void OnForegroundChanged(IntPtr foreground)
    {
        if (_disposed || !_visible || foreground == IntPtr.Zero || foreground != WindowNativeMethods.GetForegroundWindow()) return;
        if (PopupNativeMethods.BelongsToPopup(foreground, _handle)) return;
        if (foreground == _target)
        {
            _searchMode = false; SetNonActivating(true); return;
        }
        HideCore(false);
    }
    public void Hide() => HideCore(true);
    private void HideCore(bool restoreSearchTarget)
    {
        if (!_visible || _disposed) return;
        bool restore = restoreSearchTarget && _searchMode && TargetExists() &&
            PopupNativeMethods.BelongsToPopup(WindowNativeMethods.GetForegroundWindow(), _handle);
        _outsideClicks.Disarm();
        _visible = false; _searchMode = false; SetNonActivating(true);
        _window.AppWindow.Hide();
        if (restore) WindowNativeMethods.SetForegroundWindow(_target);
        Hidden?.Invoke();
    }
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) { args.Cancel = true; Hide(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outsideClicks.OutsideClicked -= OnOutsideClicked;
        _outsideClicks.Dispose();
        if (_foregroundHook != IntPtr.Zero) PopupNativeMethods.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        _window.AppWindow.Closing -= OnClosing;
        PopupNativeMethods.RemoveWindowSubclass(_handle, _subclass, SubclassId);
    }
}
