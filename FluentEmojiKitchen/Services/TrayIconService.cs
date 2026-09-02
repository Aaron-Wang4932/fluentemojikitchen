using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using System;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentEmojiKitchen.Services
{
    public sealed class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _icon;
        public event Action? ToggleRequested;
        public event Action? ExitRequested;
        public TrayIconService()
        {
            _icon = new TaskbarIcon
            {
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
                ToolTipText = "Fluent Emoji Kitchen"
            };
            _icon.ForceCreate();
            _icon.LeftClickCommand = new RelayCommand(() => ToggleRequested?.Invoke());

            var exitItem = new MenuFlyoutItem
            {
                Text = "Exit",
                Command = new RelayCommand(() => ExitRequested?.Invoke())
            };
            exitItem.Click += (_, _) => ExitRequested?.Invoke();

            var menu = new MenuFlyout();
            menu.Items.Add(exitItem);
            _icon.ContextFlyout = menu;
        }
        public void Dispose() => _icon.Dispose();
        
    }
}
