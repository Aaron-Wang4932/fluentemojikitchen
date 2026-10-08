using H.NotifyIcon;
using FluentEmojiKitchen.Helpers;
using Microsoft.UI.Xaml.Controls;
using System;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentEmojiKitchen.Views
{
    public sealed class TrayIconView : IDisposable
    {
        private readonly TaskbarIcon _icon;
        public event Action? ToggleRequested;
        public event Action? ExitRequested;
        public TrayIconView()
        {
            _icon = new TaskbarIcon
            {
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Icons/AppIcon.ico")),
                ToolTipText = "Fluent Emoji Kitchen"
            };
            _icon.ForceCreate();
            _icon.LeftClickCommand = new RelayCommand(() => ToggleRequested?.Invoke());

            var exitItem = new MenuFlyoutItem
            {
                Text = "Exit",
                Command = new RelayCommand(() => ExitRequested?.Invoke())
            };

            var menu = new MenuFlyout();
            menu.Items.Add(exitItem);
            _icon.ContextFlyout = menu;
        }
        public void Dispose() => _icon.Dispose();
        
    }
}
