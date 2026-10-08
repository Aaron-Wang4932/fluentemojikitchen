using System;
using System.Runtime.InteropServices;
namespace FluentEmojiKitchen.Interop;

internal static class WindowNativeMethods
{
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExAppWindow = 0x00040000;
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")]
    internal static extern int SetWindowLong(IntPtr handle, int index, int value);
}
