using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Interop;
using Microsoft.UI.Dispatching;
namespace FluentEmojiKitchen.Listeners;

// Observes button-down events without consuming them. No injected DLL or registration.
public sealed class OutsideClickListener : IDisposable
{
    private readonly IntPtr _popup;
    private readonly DispatcherQueue _dispatcher;
    private readonly PopupNativeMethods.HookProc _callback;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _hook;
    private uint _threadId;
    private int _armedSession, _stopping;
    public event Action<int>? OutsideClicked;
    public OutsideClickListener(IntPtr popup, DispatcherQueue dispatcher)
    {
        _popup = popup; _dispatcher = dispatcher; _callback = OnMouseInput;
        _thread = new Thread(Run) { IsBackground = true, Name = "Emoji Kitchen outside-click listener" };
        _thread.Start();
        try { _started.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }
    public void Arm(int session) => Volatile.Write(ref _armedSession, session);
    public void Disarm() => Volatile.Write(ref _armedSession, 0);
    public bool IsArmed(int session) => session != 0 && Volatile.Read(ref _armedSession) == session;
    private void Run()
    {
        try
        {
            _threadId = PopupNativeMethods.GetCurrentThreadId();
            PopupNativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Create the queue before publishing readiness.
            _hook = PopupNativeMethods.SetWindowsHookEx(14, _callback, PopupNativeMethods.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _started.TrySetResult();
            while (Volatile.Read(ref _stopping) == 0 &&
                PopupNativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                PopupNativeMethods.TranslateMessage(ref message);
                PopupNativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            System.Diagnostics.Debug.WriteLine($"Outside-click listener: {ex}");
        }
        finally
        {
            if (_hook != IntPtr.Zero) PopupNativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
    private IntPtr OnMouseInput(int code, IntPtr wp, IntPtr lp)
    {
        // Mouse moves and wheel events need no hit testing. Keep this independent of UI stalls.
        int session = Volatile.Read(ref _armedSession);
        long message = wp.ToInt64();
        if (code >= 0 && session != 0 && message is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            var data = Marshal.PtrToStructure<PopupNativeMethods.MouseHookData>(lp);
            var hit = PopupNativeMethods.WindowFromPoint(data.Position);
            if (!PopupNativeMethods.BelongsToPopup(hit, _popup) &&
                Interlocked.CompareExchange(ref _armedSession, 0, session) == session)
            {
                // Invalidating the session here prevents queued insertion while UI dismissal waits.
                _dispatcher.TryEnqueue(() =>
                {
                    if (Volatile.Read(ref _stopping) == 0) OutsideClicked?.Invoke(session);
                });
            }
        }
        return PopupNativeMethods.CallNextHookEx(_hook, code, wp, lp);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        Disarm();
        if (_threadId != 0) PopupNativeMethods.PostThreadMessage(_threadId, PopupNativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
    }
}
