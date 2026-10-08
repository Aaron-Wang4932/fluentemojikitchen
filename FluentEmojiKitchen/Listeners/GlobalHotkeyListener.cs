using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace FluentEmojiKitchen.Listeners
{
    public sealed class GlobalHotkeyListener : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_OEM_PERIOD = 0xBE;

        private readonly LowLevelKeyboardProc _hookProc;
        // Captured on construction (main/UI thread) so the hook thread can marshal
        // HotkeyPressed back onto the UI thread instead of firing it directly.
        private readonly DispatcherQueue _uiDispatcherQueue;

        private Thread? _hookThread;
        private IntPtr _hookID = IntPtr.Zero;
        private uint _hookThreadId;
        private bool _winKeyDown;

        public event Action? HotkeyPressed;

        public GlobalHotkeyListener()
        {
            _hookProc = HookCallback;
            _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public void Install()
        {
            if (_hookThread is not null) return;

            _hookThread = new Thread(HookThreadProc) { IsBackground = true };
            _hookThread.Start();
        }

        // Runs entirely on the dedicated hook thread — never touches WinUI at all.
        // Installs the hook, then pumps this thread's own message queue forever,
        // completely independent of whatever the main WinUI thread is doing.
        private void HookThreadProc()
        {
            _hookThreadId = GetCurrentThreadId();

            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule!;
            var moduleHandle = GetModuleHandle(currentModule.ModuleName);

            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

            // A thread with a low-level hook must have a message loop, per the
            // hook's own documented contract — this keeps the thread alive and
            // responsive until Uninstall() posts WM_QUIT to it.
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        public void Uninstall()
        {
            if (_hookThread is null) return;

            // Wakes the GetMessage loop above so it exits and unhooks itself.
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(timeout: TimeSpan.FromMilliseconds(1000));
            _hookThread = null;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var msg = wParam.ToInt32();

                if (info.vkCode == VK_LWIN || info.vkCode == VK_RWIN)
                {
                    _winKeyDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                }
                else if (_winKeyDown
                         && info.vkCode == VK_OEM_PERIOD
                         && msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    // We're on the hook thread here — marshal onto the UI thread
                    // rather than invoking HotkeyPressed directly.
                    _uiDispatcherQueue.TryEnqueue(() => HotkeyPressed?.Invoke());
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose() => Uninstall();

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private const uint WM_QUIT = 0x0012;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
