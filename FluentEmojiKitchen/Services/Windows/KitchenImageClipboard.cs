using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
namespace FluentEmojiKitchen.Services.Windows;

// Native clipboard ownership uses the popup HWND, without activating the popup.
public sealed class KitchenImageClipboard : IKitchenImageClipboard
{
    private readonly IntPtr _owner;
    public KitchenImageClipboard(IntPtr owner) => _owner = owner;

    public async Task CopyAsync(Uri source, int pixelSize, CancellationToken cancellationToken)
    {
        if (!source.IsFile) throw new ArgumentException("A cached image file is required.", nameof(source));
        if (pixelSize is not (48 or 128 or 256 or 512 or 96)) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        cancellationToken.ThrowIfCancellationRequested();
        var file = await StorageFile.GetFileFromPathAsync(source.LocalPath);
        using var input = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(input);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)pixelSize, ScaledHeight = (uint)pixelSize,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var provider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        byte[] pixels = provider.DetachPixelData();
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            (uint)pixelSize, (uint)pixelSize, 96, 96, pixels);
        await encoder.FlushAsync();
        output.Seek(0);
        byte[] png = new byte[checked((int)output.Size)];
        using (var reader = new DataReader(output.GetInputStreamAt(0)))
        {
            uint read = await reader.LoadAsync((uint)png.Length);
            if (read != png.Length) throw new InvalidOperationException("Incomplete PNG encoding.");
            reader.ReadBytes(png);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Offer PNG plus a standard top-down 32-bit DIBV5 for bitmap-only paste targets.
        byte[] dib = new byte[124 + pixels.Length];
        void UInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(dib, offset);
        UInt(0, 124); UInt(4, (uint)pixelSize); UInt(8, unchecked((uint)-pixelSize));
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        UInt(16, 3); UInt(20, (uint)pixels.Length); // BI_BITFIELDS
        UInt(24, 3780); UInt(28, 3780); // 96 DPI
        UInt(40, 0x00ff0000); UInt(44, 0x0000ff00); UInt(48, 0x000000ff); UInt(52, 0xff000000);
        UInt(56, 0x73524742); UInt(108, 4); // sRGB, LCS_GM_IMAGES
        pixels.CopyTo(dib, 124);
        uint pngFormat = RegisterClipboardFormat("PNG");
        if (pngFormat == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr pngHandle = IntPtr.Zero, dibHandle = IntPtr.Zero;
        bool opened = false;
        try
        {
            pngHandle = Allocate(png); dibHandle = Allocate(dib);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (OpenClipboard(_owner)) { opened = true; break; }
                await Task.Delay(25, cancellationToken);
            }
            if (!opened) throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard is busy.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (SetClipboardData(pngFormat, pngHandle) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            pngHandle = IntPtr.Zero; // Windows now owns this allocation.
            if (SetClipboardData(17, dibHandle) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            dibHandle = IntPtr.Zero; // CF_DIBV5
        }
        finally
        {
            if (opened) CloseClipboard();
            if (pngHandle != IntPtr.Zero) GlobalFree(pngHandle);
            if (dibHandle != IntPtr.Zero) GlobalFree(dibHandle);
        }
    }
    private static IntPtr Allocate(byte[] bytes)
    {
        IntPtr handle = GlobalAlloc(0x0002, (UIntPtr)bytes.Length); // GMEM_MOVEABLE
        if (handle == IntPtr.Zero) throw new OutOfMemoryException();
        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) { GlobalFree(handle); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        catch { GlobalUnlock(handle); GlobalFree(handle); throw; }
        GlobalUnlock(handle);
        return handle;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
