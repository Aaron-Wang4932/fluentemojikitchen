using H.NotifyIcon;
using Microsoft.UI.Xaml;
using System;

namespace FluentEmojiKitchen
{
    public partial class App : Application
    {
        private Window? _window;
        private TaskbarIcon? _trayIcon;

        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _trayIcon = new TaskbarIcon
            {
                IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri("ms-appx:///Assets/TrayIcon.ico")
                ),
                ToolTipText = "Fluent Emoji Kitchen"
            };

            _trayIcon.ForceCreate();
            _trayIcon.LeftClickCommand = new RelayCommand(TogglePopup);
            // set up tray menu buttons
            var exitItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Exit" };
            exitItem.Click += (_, _) =>
            {
                _trayIcon?.Dispose();
                Environment.Exit(0);
            };
            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            menu.Items.Add(exitItem);
            _trayIcon.ContextFlyout = menu;
        }

        private void TogglePopup()
        {
            if(_window is null)
            {
                _window = new MainWindow();
                _window.Activate();
            } else
            {
                _window.Close();
                _window = null;
            }
        }
    }
}
