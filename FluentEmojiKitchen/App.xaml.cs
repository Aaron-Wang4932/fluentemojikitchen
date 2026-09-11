using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using System.IO;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TrayIconService? _trayIconService;
        private GlobalHotkeyService? _globalHotkeyService;
        private const uint FR_PRIVATE = 0x10;

        public App() => InitializeComponent();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            RegisterEmojiFont();

            _globalHotkeyService = new GlobalHotkeyService();
            _globalHotkeyService.HotkeyPressed += TogglePopup;
            _globalHotkeyService.Install();

            // try to restore prev. registry values if app crashes or something.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => _globalHotkeyService?.Dispose();

            _trayIconService = new TrayIconService();
            _trayIconService.ToggleRequested += TogglePopup;
            _trayIconService.ExitRequested += () =>
            {
                _globalHotkeyService.Dispose();
                _trayIconService?.Dispose();
                Environment.Exit(0);
            };

            _window = new MainWindow();
        }

        private void TogglePopup()
        {
            _window!.TogglePopupVisibility();
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        private static void RegisterEmojiFont()
        {
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoColorEmoji.ttf");
            if (File.Exists(fontPath))
            {
                AddFontResourceEx(fontPath, FR_PRIVATE, IntPtr.Zero);
            }
        }
    }
}