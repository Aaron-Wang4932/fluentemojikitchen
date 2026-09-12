using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TrayIconService? _trayIconService;
        private GlobalHotkeyService? _globalHotkeyService;

        public App() 
        {
            // to fix issue where devices with non-functional ipv6 address will wait for download to time out before trying ipv4
            AppContext.SetSwitch("System.Net.DisableIPv6", true); 
            InitializeComponent(); 
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _globalHotkeyService = new GlobalHotkeyService();
            _globalHotkeyService.HotkeyPressed += TogglePopup;
            _globalHotkeyService.Install();

            // try to remove the win+. keybind hook if app crashes or something.
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
            _ = InitAsync();
        }

        private void TogglePopup()
        {
            _window!.TogglePopupVisibility();
        }

        private async Task InitAsync()
        {
            try
            {
                var emojiDataService = await EmojiDataService.CreateAsync();
                await _window!.SetEmojiData(emojiDataService);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Emoji data initialization failed: {ex}");
            }
        }
    }
}