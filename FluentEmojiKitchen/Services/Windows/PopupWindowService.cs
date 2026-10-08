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
        PositionNearCaret();
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

    private void PositionNearCaret()
    {
        var info = new CaretThreadInfo
        {
            cbSize = (uint)Marshal.SizeOf<CaretThreadInfo>()
        };

        NativePoint top;
        NativePoint bottom;

        bool foundCaret =
            GetGUIThreadInfo(_targetThread, ref info) &&
            info.hwndCaret != IntPtr.Zero &&
            info.rcCaret.Bottom > info.rcCaret.Top;

        if (foundCaret)
        {
            top = new NativePoint
            {
                X = info.rcCaret.Left,
                Y = info.rcCaret.Top
            };

            bottom = new NativePoint
            {
                X = info.rcCaret.Left,
                Y = info.rcCaret.Bottom
            };

            foundCaret =
                ClientToScreen(info.hwndCaret, ref top) &&
                ClientToScreen(info.hwndCaret, ref bottom);
        }
        else
        {
            top = bottom = default;
        }

        if (!foundCaret)
        {
            if (!GetCursorPos(out bottom))
                return;

            top = bottom;
        }

        var anchor = new global::Windows.Graphics.PointInt32(
            bottom.X, bottom.Y);

        var display = DisplayArea.GetFromPoint(
            anchor, DisplayAreaFallback.Nearest);

        var work = display.WorkArea;
        var size = _window.AppWindow.Size;

        const int gap = 8; // Screen pixels.

        int x = bottom.X;
        int y = bottom.Y + gap;

        // Prefer above the caret if the picker would extend below the screen.
        if (y + size.Height > work.Y + work.Height)
            y = top.Y - size.Height - gap;

        // Keep the picker within this monitor's work area.
        // Math.Max also handles a picker larger than the available area.
        x = Math.Clamp(
            x,
            work.X,
            Math.Max(work.X, work.X + work.Width - size.Width));

        y = Math.Clamp(
            y,
            work.Y,
            Math.Max(work.Y, work.Y + work.Height - size.Height));

        _window.AppWindow.Move(
            new global::Windows.Graphics.PointInt32(x, y));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CaretThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeRect rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(
        uint threadId, ref CaretThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(
        IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
