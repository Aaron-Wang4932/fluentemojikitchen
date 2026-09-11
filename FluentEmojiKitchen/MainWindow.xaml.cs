using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System;
using System.IO;

namespace FluentEmojiKitchen
{
    public sealed partial class MainWindow : Window
    {
        private bool _isPopupVisible = false; // initially the window is not visible.
        // Calling AppWindow.Show()/Hide() can itself trigger Activated event handler while TogglePopupVisibility is still running.
        // We set a flag to indicate that we are still in the midst of changing states.
        private bool _isProgrammaticToggle;

        public MainWindow()
        {
            // Initialize contents in MainWindow.xaml
            this.InitializeComponent();
            // Makes TabStripGrid in the associated .xaml the title bar (enables repositioning of window)
            SetTitleBar(TabStripGrid);

            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            // Removes the native system bar.
            ExtendsContentIntoTitleBar = true;

            // Configuration of window position and sizing
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsAlwaysOnTop = true;
            }

            AppWindow.Resize(new Windows.Graphics.SizeInt32(360, 420));

            AppWindow.Closing += OnAppWindowClosing;
            this.Activated += OnWindowActivated;
            SetAppIcon();
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // Cancels actually closing the application.
            args.Cancel = true;
            TogglePopupVisibility();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_isProgrammaticToggle) return; // if we are in the midst of toggling, dont toggle again

            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                TogglePopupVisibility();
            }
        }

        public void TogglePopupVisibility()
        {
            _isProgrammaticToggle = true;

            if (_isPopupVisible) 
                AppWindow.Hide();
            else
            {
                SetAllFocus();
                AppWindow.Show();
            }

            _isPopupVisible = !_isPopupVisible;

            // Vs. resetting the flag synchronously, inline; guard will have expired and will not catch the bug outlined above.
            DispatcherQueue.TryEnqueue(() => _isProgrammaticToggle = false);
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e) => TogglePopupVisibility();

        private void SetAllFocus()
        {
            // another way to get window focus from background state lol (the above is kinda inconsistent)
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetForegroundWindow(hWnd);
            // Then, focus the emoji search bar.
            DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));
        }

        private void SetAppIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/Icons/AppIcon.ico");
            if (File.Exists(iconPath))
                this.AppWindow.SetIcon(iconPath);
        }

        private void OnEmojiTabClick(object sender, RoutedEventArgs e)
        {
            EmojiTabButton.IsChecked = true;
            KitchenTabButton.IsChecked = false;
            EmojiTabContent.Visibility = Visibility.Visible;
            KitchenTabContent.Visibility = Visibility.Collapsed;
        }

        private void OnKitchenTabClick(object sender, RoutedEventArgs e)
        {
            EmojiTabButton.IsChecked = false;
            KitchenTabButton.IsChecked = true;
            EmojiTabContent.Visibility = Visibility.Collapsed;
            KitchenTabContent.Visibility = Visibility.Visible;
        }


        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}