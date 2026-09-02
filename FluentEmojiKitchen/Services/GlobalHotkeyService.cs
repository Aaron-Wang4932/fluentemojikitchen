using System;
using System.Runtime.InteropServices;

namespace FluentEmojiKitchen.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYUP = 0x0101;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_OEM_PERIOD = 0xBE;

        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr _hookID = IntPtr.Zero;
        private bool _winKeyDown;

        public event Action? HotkeyPressed;

        public GlobalHotkeyService() => _hookProc = HookCallback;

        public void Install()
        {
            if (_hookID != IntPtr.Zero) return;
            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule!;
            var moduleHandle = GetModuleHandle(currentModule.ModuleName);

            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
        }

        public void Dispose() // ie. Uninstall
        {
            if (_hookID == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
            
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
                    HotkeyPressed?.Invoke();
                    return (IntPtr)1; // non-zero return = swallow the keystroke
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
