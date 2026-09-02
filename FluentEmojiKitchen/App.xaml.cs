using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml;
using System;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TrayIconService? _trayIconService;
        private GlobalHotkeyService? _globalHotkeyService;

        public App() => InitializeComponent();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
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
        }

        private void TogglePopup()
        {
            if (_window is null)
            {
                _window = new MainWindow();
                _window.Activate();
                return;
            }

            _window.TogglePopupVisibility();
        }
    }
}