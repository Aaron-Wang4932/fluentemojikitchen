using System.Runtime.InteropServices;

namespace FluentEmojiKitchen.Services
{
    public static class EmojiInsertionService
    {
        public static void InsertText(string text)
        {
            // Each INPUT struct = one keydown or keyup. Surrogate-pair emoji are two
            // UTF-16 chars, so we just iterate chars — Windows re-pairs them correctly
            // as long as the down events are sent back-to-back in order.
            var inputs = new INPUT[text.Length * 2];
            int idx = 0;

            foreach (char c in text)
            {
                inputs[idx++] = CreateUnicodeInput(c, keyUp: false); // simulate a keydown and keyup press for each codepoint in the emoji
                inputs[idx++] = CreateUnicodeInput(c, keyUp: true);
            }

            uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (result != inputs.Length)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SendInput only sent {result}/{inputs.Length} events. Error: {Marshal.GetLastWin32Error()}");
            }
        }

        private static INPUT CreateUnicodeInput(char c, bool keyUp) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = System.IntPtr.Zero
                }
            }
        };

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public System.IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public System.IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
    }
}