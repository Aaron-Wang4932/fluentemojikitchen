using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml;
using System;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TrayIconService? _trayIconService;
        private HotkeyRegistryService? _hotkeyRegistryService;

        public App() => InitializeComponent();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _hotkeyRegistryService = new HotkeyRegistryService();
            _hotkeyRegistryService.RecoverFromCrashIfNeeded();
            _hotkeyRegistryService.DisableOsHotkey();

            // try to restore prev. registry values if app crashes or something.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => _hotkeyRegistryService.RestoreOsHotkey();

            _trayIconService = new TrayIconService();
            _trayIconService.ToggleRequested += TogglePopup;
            _trayIconService.ExitRequested += () =>
            {
                _hotkeyRegistryService.RestoreOsHotkey();
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