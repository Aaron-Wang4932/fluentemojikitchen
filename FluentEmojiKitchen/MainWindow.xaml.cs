using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FluentEmojiKitchen
{
    public sealed partial class MainWindow : Window
    {
        // ===== Fields =====

        // Initial state upon loading: window not visible.
        private bool _isPopupVisible = false;

        // Calling AppWindow.Show()/Hide() can itself trigger Activated event handler while TogglePopupVisibility is still running.
        // We set a flag to indicate that we are still in the midst of changing states.
        private bool _isProgrammaticToggle;

        private Models.EmojiItemViewModel? _selectedEmoji;
        private IntPtr _previousForegroundWindow;

        // Queued emoji characters awaiting insertion into the previously-focused app.
        private readonly Queue<string> _pendingInserts = new();
        private bool _isProcessingInsertQueue;

        // ===== Construction / window setup =====

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
            AppWindow.IsShownInSwitchers = false; // hide from Alt+Tab

            // Hide from the taskbar itself
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);

            AppWindow.Closing += OnAppWindowClosing;
            this.Activated += OnWindowActivated;
            SetAppIcon();
        }

        private void SetAppIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/Icons/AppIcon.ico");
            if (File.Exists(iconPath))
                this.AppWindow.SetIcon(iconPath);
        }

        // ===== Show / hide lifecycle =====

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
                _previousForegroundWindow = GetForegroundWindow();
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

        // ===== Tab switching =====

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

        // ===== Emoji grid: data + hover visuals =====

        //public void SetEmojiData(EmojiDataService emojiDataService)
        //{
        //    LoadingText.Visibility = Visibility.Collapsed;
        //    EmojiTabContent.Visibility = Visibility.Visible;

        //    // Remove the display of skin-tonal emojis; only show the base variant (improves performance)
        //    EmojiRepeater.ItemsSource = emojiDataService.Emojis
        //        .Where(IsBaseVariant)
        //        .Select(e => new EmojiItemViewModel(e))
        //        .ToList();
        //}

        public async Task SetEmojiData(EmojiDataService emojiDataService)
        {
            // Keep "Loading emoji..." visible & EmojiTabContent transparent to allow for glyphs to precache
            EmojiTabContent.Visibility = Visibility.Visible;
            //EmojiTabContent.Opacity = 0.004; // lowest possible opacity before 0; making it 0 makes the glyphs not precache

            EmojiRepeater.ItemsSource = emojiDataService.Emojis
                .Where(e => IsBaseVariant(e) && !IsGenderedVariation(e))
                .Select(e => new Models.EmojiItemViewModel(e))
                .ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await WarmUpEmojiGridAsync();
            System.Diagnostics.Debug.WriteLine($"Warmup took {sw.ElapsedMilliseconds}ms");

            LoadingOverlay.Visibility = Visibility.Collapsed;
            //EmojiTabContent.Opacity = 1;

            System.Diagnostics.Debug.WriteLine(emojiDataService.Emojis.Count(e => IsBaseVariant(e) && !IsGenderedVariation(e)) + " total emojis after filter.");
        }

        private async Task WarmUpEmojiGridAsync()
        {
            // Let one layout pass happen so ScrollableHeight is actually valid.
            await Task.Delay(50);

            double viewportHeight = EmojiTabContent.ViewportHeight;
            double maxOffset = EmojiTabContent.ScrollableHeight;

            if (viewportHeight <= 0 || maxOffset <= 0)
                return; // nothing to warm up (e.g. window not yet sized)

            int totalProgressBarTicks = (int)Math.Ceiling(maxOffset / viewportHeight);
            int curProgressBarTick = 0;

            // Step through the full scrollable range in viewport-sized increments to precache everything.
            // Cannot immediately scroll from very top to very bottom since the glyphs in the middle would not get cached.
            for (double offset = 0; offset < maxOffset; offset += viewportHeight) // ? += viewportHeight
            {
                EmojiTabContent.ChangeView(null, offset, null, disableAnimation: true);
                await Task.Delay(15);

                curProgressBarTick++;
                LoadingProgressBar.Value = (double)curProgressBarTick / totalProgressBarTicks * 100;
            }

            EmojiTabContent.ChangeView(null, 0, null, disableAnimation: true);
            await Task.Delay(15);

            LoadingProgressBar.Value = 100;
        }

        private static bool IsBaseVariant(EmojiModel emoji)
        {
            // Fitzpatrick skin-tone modifiers occupy this exact codepoint range.
            // Any emoji containing one is a toned variant of some other base entry.
            return !emoji.Codepoints.Any(cp => cp is >= 0x1F3FB and <= 0x1F3FF);
        }

        private static bool IsGenderedVariation(EmojiModel emoji)
        {
            return emoji.Codepoints.Any(cp => (cp == 0x2642 || cp == 0x2640)) // gendered
                && emoji.Codepoints.Contains(0x200D) // zero-width joiner
                && emoji.Codepoints.Contains(0xFE0F); // variation selector
        }

        private void OnEmojiPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement root && root.FindName("HoverLayer") is Border hover)
                hover.Opacity = 1;
        }

        private void OnEmojiPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement root && root.FindName("HoverLayer") is Border hover)
                hover.Opacity = 0;
        }

        // ===== Emoji click -> insertion queue =====

        private void OnEmojiItemClick(object sender, TappedRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("click detected");
            if (sender is FrameworkElement { Tag: Models.EmojiItemViewModel vm })
            {
                // Emoji selection border (appearance) logic
                if (_selectedEmoji != vm && _selectedEmoji is not null)
                {
                    _selectedEmoji.IsSelected = false;
                }
                vm.IsSelected = true;
                _selectedEmoji = vm;

                System.Diagnostics.Debug.WriteLine(vm.Character);

                _pendingInserts.Enqueue(vm.Character);
                if (!_isProcessingInsertQueue)
                    _ = ProcessInsertQueueAsync();
            }
        }

        private async Task ProcessInsertQueueAsync()
        {
            _isProcessingInsertQueue = true;
            _isProgrammaticToggle = true;

            // Foreground the target once for the whole batch, not once per emoji —
            // repeated redundant foregrounding mid-batch is itself a plausible source
            // of interrupting the target's in-progress composition.
            SetForegroundWindow(_previousForegroundWindow);
            await Task.Delay(60);

            while (_pendingInserts.Count > 0)
            {
                string character = _pendingInserts.Dequeue();
                EmojiInsertionService.InsertText(character);

                // Give the target's text/composition layer time to fully finish
                // reassembling this emoji before the next one's keystrokes arrive.
                await Task.Delay(40);
            }

            _isProcessingInsertQueue = false;
            DispatcherQueue.TryEnqueue(() => _isProgrammaticToggle = false);
        }

        // ===== Win32 interop =====

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}