using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FluentEmojiKitchen
{
    public sealed partial class MainWindow : Window
    {
        private bool _isPopupVisible = true;

        public MainWindow()
        {
            this.InitializeComponent();
            AppWindow.Closing += OnAppWindowClosing;
            AppWindow.Changed += OnAppWindowFocus;
            
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;   // don't actually destroy the window/process
            TogglePopupVisibility();
        }

        private void OnAppWindowFocus(AppWindow sender, AppWindowChangedEventArgs args)
        {
            var presenter = sender.Presenter as OverlappedPresenter;
            if (presenter == null) return;

            if (args.DidSizeChange && presenter.State == OverlappedPresenterState.Minimized)
            {
                TogglePopupVisibility();
            }
        }

        public void TogglePopupVisibility()
        {
            if (_isPopupVisible)
            {
                AppWindow.Hide();
            }
            else
            {
                AppWindow.Show();
                this.Activate();
            }

            _isPopupVisible = !_isPopupVisible;
        }
    }
}