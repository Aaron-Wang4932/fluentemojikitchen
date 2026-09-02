using FluentEmojiKitchen.Services;
using Microsoft.UI.Xaml;
using System;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private TrayIconService? _trayIconService;

        public App() => InitializeComponent();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _trayIconService = new TrayIconService();
            _trayIconService.ToggleRequested += TogglePopup;
            _trayIconService.ExitRequested += () =>
            {
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