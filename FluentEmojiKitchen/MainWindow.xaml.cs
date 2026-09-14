using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentEmojiKitchen.Helpers;

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

        private bool doEmojiPrecaching = true;
        private EmojiDataService? _emojiDataService;

        // Backing collection for EmojiRepeater.ItemsSource. Mutated in place to prevent full re-virtualization
        private ObservableCollection<EmojiSectionViewModel>? _sections;

        // Cached lookup from character -> EmojiModel so rebuilding Recent section doesn't need to read through entire emoji list
        private Dictionary<string, EmojiModel>? _emojiByCharacter;

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

            AppWindow.Resize(new Windows.Graphics.SizeInt32(360, 480));
            AppWindow.IsShownInSwitchers = false; // hide from Alt+Tab

            // Hide from the taskbar, AND prevent this window from ever taking activation.
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
            {
                AppWindow.Hide();
                EmojiTabContent.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled)); // scroll to top
                EmojiPanelSearchBox.Text = string.Empty; // clear searchbar
                _selectedEmoji?.IsSelected = false; // reset border on selected 
            }

            else
            {
                // bringing back our emoji panel: remember prev. foreground window to keep track of where cursor was
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
            DispatcherQueue.TryEnqueue(() => EmojiPanelSearchBox.Focus(FocusState.Programmatic));
        }

        // ===== Tab switching =====

        private void OnEmojiTabClick(object sender, RoutedEventArgs e)
        {
            EmojiTabButton.IsChecked = true;
            KitchenTabButton.IsChecked = false;
            KitchenTabContent.Visibility = Visibility.Collapsed;
            EmojiPanelSearchBox.Visibility = Visibility.Visible;

            // Restore whichever emoji view (grouped vs. flat search results) matches the search box's current text
            UpdateEmojiView();
        }

        private void OnKitchenTabClick(object sender, RoutedEventArgs e)
        {
            EmojiTabButton.IsChecked = false;
            KitchenTabButton.IsChecked = true;
            EmojiTabContent.Visibility = Visibility.Collapsed;
            SearchResultsContent.Visibility = Visibility.Collapsed;
            KitchenTabContent.Visibility = Visibility.Visible;
            EmojiPanelSearchBox.Visibility = Visibility.Collapsed;
        }

        // ===== Emoji grid: data + hover visuals =====

        public async Task SetEmojiData(EmojiDataService emojiDataService)
        {
            // Keep "Loading emoji..." visible & EmojiTabContent transparent to allow for glyphs to precache
            EmojiTabContent.Visibility = Visibility.Visible;

            _emojiDataService = emojiDataService;
            _emojiByCharacter = _emojiDataService.Emojis.ToDictionary(e => e.Character);

            RefreshGroupedView();

            if (doEmojiPrecaching)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await WarmUpEmojiGridAsync();
                System.Diagnostics.Debug.WriteLine($"Warmup took {sw.ElapsedMilliseconds}ms");
            }
            LoadingProgressBar.Value = 100;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            EmojiPanelSearchBox.Visibility = Visibility.Visible;

            System.Diagnostics.Debug.WriteLine(_emojiDataService.Emojis.Count(e => !EmojiFilterHelper.IsSkinToneVariant(e)) + " total emojis after filter.");
            ScrollToTopButton.Visibility = Visibility.Visible;
        }

        // Groups already-filtered emoji into sections by (Group, Subgroup), in the
        // order they appear in the source list; matches emoji-test.txt from Unicode.
        private static List<EmojiSectionViewModel> BuildSections(IEnumerable<EmojiModel> emojis)
        {
            var sections = new List<EmojiSectionViewModel>();
            string? lastGroup = null;

            foreach (var g in emojis.GroupBy(e => (e.Group, e.Subgroup)))
            {
                bool isFirstInGroup = g.Key.Group != lastGroup;
                lastGroup = g.Key.Group;

                sections.Add(new EmojiSectionViewModel(
                    g.Key.Group,
                    g.Key.Subgroup,
                    isFirstInGroup,
                    g.Select(e => new Models.EmojiItemViewModel(e)).ToList()));
            }

            return sections;
        }

        private EmojiSectionViewModel? BuildRecentEmojiSection()
        {
            if (_emojiDataService is null || _emojiByCharacter is null || _emojiDataService.RecentCharacters.Count == 0)
                return null;

            var recentItems = _emojiDataService.RecentCharacters
                .Where(_emojiByCharacter.ContainsKey) // guard against a stale/invalid entry if the underlying emoji data ever changes
                .Select(c => new Models.EmojiItemViewModel(_emojiByCharacter[c]))
                .ToList();

            return new EmojiSectionViewModel(
                "Recent",
                $"Showing {recentItems.Count}/24 recent",
                isFirstInGroup: true,
                recentItems);
        }

        // Full (re)build — called once when data loads, or when the underlying emoji
        // set itself changes. Not used for recent-adds; see UpdateRecentSection.
        private void RefreshGroupedView()
        {
            if (_emojiDataService is null) return;

            _sections = new ObservableCollection<EmojiSectionViewModel>();

            var recentSection = BuildRecentEmojiSection();
            if (recentSection is not null)
                _sections.Add(recentSection);

            foreach (var section in BuildSections(
                _emojiDataService.Emojis.Where(e => !EmojiFilterHelper.IsSkinToneVariant(e))))
            {
                _sections.Add(section);
            }

            EmojiRepeater.ItemsSource = _sections;
        }

        // Called on every add to recent emojis, mutates the existing Recent section (or inserts one)
        // in place rather than reassigning ItemsSource so we don't need to do a full re-virtualization
        private void UpdateRecentSection()
        {
            if (_sections is null) return;

            var recent = BuildRecentEmojiSection();
            if (recent is null) return;

            if (_sections.Count > 0 && _sections[0].GroupName == "Recent")
            {
                var existing = _sections[0];
                existing.SubgroupName = recent.SubgroupName;

                existing.Emojis.Clear();
                foreach (var item in recent.Emojis)
                    existing.Emojis.Add(item);
            }
            else
            {
                _sections.Insert(0, recent);
            }
        }

        private async Task WarmUpEmojiGridAsync()
        {
            //LoadingOverlay.Visibility = Visibility.Collapsed;
            var scrollOptions = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled);
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
            for (double offset = 0; offset < maxOffset; offset += viewportHeight)
            {
                EmojiTabContent.ScrollTo(EmojiTabContent.HorizontalOffset, offset, scrollOptions);
                await Task.Delay(15);
                System.Diagnostics.Debug.WriteLine(viewportHeight);

                curProgressBarTick++;
                LoadingProgressBar.Value = (double)curProgressBarTick / totalProgressBarTicks * 100;
            }

            EmojiTabContent.ScrollTo(0, 0, scrollOptions);
            await Task.Delay(350);
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

                System.Diagnostics.Debug.WriteLine(vm.Model);

                _pendingInserts.Enqueue(vm.Character);
                _emojiDataService?.AddRecent(vm.Character);
                UpdateRecentSection();
                if (!_isProcessingInsertQueue)
                    _ = ProcessInsertQueueAsync();
            }
        }

        private async Task ProcessInsertQueueAsync()
        {
            _isProcessingInsertQueue = true;
            _isProgrammaticToggle = true;

            // Foreground the target once for. the whole batch, not once per emoji
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
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)); // give focus back to emoji panel so that we can do the whole "lose focus to turn off panel" thing
            _isProcessingInsertQueue = false;
            DispatcherQueue.TryEnqueue(() => _isProgrammaticToggle = false);
        }

        // ===== Search box =====

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => UpdateEmojiView();

        // Shows the grouped view when the search box is empty, or a flat filtered
        // grid when it has text — called both on text change and when switching
        // back to the Emoji tab, so the two stay in sync regardless of entry point.
        private void UpdateEmojiView()
        {
            string query = EmojiPanelSearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                EmojiTabContent.Visibility = Visibility.Visible;
                SearchResultsContent.Visibility = Visibility.Collapsed;
                return;
            }

            var filtered = _emojiDataService!.Emojis.Where(emoji =>
                emoji.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                emoji.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                emoji.Subgroup.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (emoji.Keywords?.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false) // match query, group, subgroup, keywords.
            ).ToList();

            SearchResultsRepeater.ItemsSource = filtered
                .Where(e => !EmojiFilterHelper.IsSkinToneVariant(e)) // again, filter out all the skin tones and convert each EmojiModel to the actual tiles on the grid.
                .Select(e => new Models.EmojiItemViewModel(e)).ToList();

            EmojiTabContent.Visibility = Visibility.Collapsed;
            SearchResultsContent.Visibility = Visibility.Visible;
        }

        // ===== All things scroll: =====
        private void OnEmojiTabContentScrolled(object sender, object e)
        {
            bool shouldShow = EmojiTabContent.VerticalOffset > 200;
            ScrollToTopButton.Opacity = shouldShow ? 1 : 0;
            ScrollToTopButton.IsHitTestVisible = shouldShow;

            //System.Diagnostics.Debug.WriteLine(EmojiTabContent.VerticalOffset);
        }

        private void OnScrollToTopClick(object sender, RoutedEventArgs e)
        {
            EmojiTabContent.ScrollTo(EmojiTabContent.HorizontalOffset, 0,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
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