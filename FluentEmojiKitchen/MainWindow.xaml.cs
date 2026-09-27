using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
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
        /*****
         Fields
         *****/

        // Initial state
        private bool _isPopupVisible = false;

        // Calling AppWindow.Show()/Hide() can itself trigger Activated event handler while TogglePopupVisibility is still running.
        // We set a flag to indicate that we are still in the midst of changing states.
        private bool _isProgrammaticToggle;

        private IntPtr _previousForegroundWindow;

        private Models.EmojiItemViewModel? _selectedEmoji;

        // Queued emoji characters awaiting insertion into the previously-focused app.
        private readonly Queue<string> _pendingInserts = new();
        private bool _isProcessingInsertQueue;
        private EmojiDataService? _emojiDataService;

        // Backing collection for EmojiRepeater.ItemsSource. Mutated in place to prevent full re-virtualization
        private ObservableCollection<EmojiSectionViewModel>? _sections;

        // Cached lookup from character -> EmojiModel so rebuilding Recent section doesn't need to read through entire emoji list
        private Dictionary<string, EmojiModel>? _emojiByCharacter;

        private bool doEmojiPrecaching = false;

        // ===== Kitchen tab state =====
        private enum KitchenSlot { None, Left, Right }
        private KitchenSlot _activeKitchenSlot = KitchenSlot.None; // which slot's picker is currently open, if any
        private Models.EmojiItemViewModel? _kitchenLeftEmoji;
        private Models.EmojiItemViewModel? _kitchenRightEmoji;

        // ===== Construction / window setup =====

        public MainWindow()
        {
            // Initialize contents in MainWindow.xaml
            this.InitializeComponent();


            SetTitleBar(TabStripGrid);
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            ExtendsContentIntoTitleBar = true; // Removes the native system bar.

            // Configuration of window position and sizing
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                //presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsAlwaysOnTop = true;
            }

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            CharacterizeWindowAsPopup(hWnd);

            AppWindow.Closing += OnAppWindowClosing;
            this.Activated += OnWindowActivated;
            SetAppIcon();

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(360, (int)(displayArea.WorkArea.Height * (0.75)))); // set maximal height to bolster precaching
        }
        private void CharacterizeWindowAsPopup(IntPtr hWnd)
        {
            AppWindow.IsShownInSwitchers = false; // hide from Alt+Tab
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        }

        private void SetAppIcon()
        {
            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets/Icons/AppIcon.ico");
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
                KitchenSearchBox.Text = string.Empty; // clear kitchen searchbar too
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

            UpdateKitchenView();
        }

        // ===== Kitchen tab =====

        // Tapping a slot opens its picker at the bottom; tapping the already-active
        // slot again closes the picker (deselects), leaving whatever emoji (if any)
        // was already chosen in place.
        private void OnKitchenSlotTapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tagStr }) return;
            var slot = tagStr == "Left" ? KitchenSlot.Left : KitchenSlot.Right;

            _activeKitchenSlot = _activeKitchenSlot == slot ? KitchenSlot.None : slot;
            UpdateKitchenView();
        }

        // Picking an emoji from the Kitchen picker fills whichever slot is currently
        // active and closes the picker (mirrors tapping the slot again).
        private void OnKitchenPickerEmojiClick(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: Models.EmojiItemViewModel vm }) return;
            if (_activeKitchenSlot == KitchenSlot.None) return;

            if (_activeKitchenSlot == KitchenSlot.Left)
                _kitchenLeftEmoji = vm;
            else
                _kitchenRightEmoji = vm;

            _activeKitchenSlot = KitchenSlot.None;
            UpdateKitchenView();
        }

        // The little X on a filled slot: clears just that slot back to empty. Uses Tapped (not
        // Click) so it can mark the event Handled -- otherwise the tap would bubble up to the
        // slot Grid's own Tapped handler and immediately re-open/toggle that slot's picker.
        private void OnKitchenSlotClearTapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not FrameworkElement { Tag: string tagStr }) return;

            if (tagStr == "Left")
                _kitchenLeftEmoji = null;
            else
                _kitchenRightEmoji = null;

            UpdateKitchenView();
        }

        // What the Kitchen tab looks like: slot contents/highlight, plus which of {hint, picker, merged result} the bottom area shows.
        private void UpdateKitchenView()
        {
            SetKitchenSlotVisual(KitchenSlotLeftPlus, KitchenSlotLeftEmoji, KitchenSlotLeftBorder, KitchenSlotLeftClearButton,
                _kitchenLeftEmoji, _activeKitchenSlot == KitchenSlot.Left);
            SetKitchenSlotVisual(KitchenSlotRightPlus, KitchenSlotRightEmoji, KitchenSlotRightBorder, KitchenSlotRightClearButton,
                _kitchenRightEmoji, _activeKitchenSlot == KitchenSlot.Right);

            bool bothFilled = _kitchenLeftEmoji is not null && _kitchenRightEmoji is not null;

            if (_activeKitchenSlot != KitchenSlot.None)
            {
                // A slot is being (re)picked — always show the picker, even if both
                // slots already held emoji from a previous pick.
                KitchenPickerRepeater.ItemsSource ??= _emojiDataService is not null
                    ? BuildKitchenSections(_emojiDataService.SupportedFusionEmojis)
                    : null;

                KitchenHintText.Visibility = Visibility.Collapsed;
                KitchenPickerContent.Visibility = Visibility.Visible;
                KitchenResultContent.Visibility = Visibility.Collapsed;
            }
            else if (bothFilled)
            {
                KitchenHintText.Visibility = Visibility.Collapsed;
                KitchenPickerContent.Visibility = Visibility.Collapsed;
                KitchenResultContent.Visibility = Visibility.Visible;
                _ = LoadKitchenMergedResultAsync();
            }
            else
            {
                KitchenHintText.Visibility = Visibility.Visible;
                KitchenPickerContent.Visibility = Visibility.Collapsed;
                KitchenResultContent.Visibility = Visibility.Collapsed;
            }
        }

        private static void SetKitchenSlotVisual(TextBlock plusText, TextBlock emojiText, Rectangle border, Button clearButton,
            Models.EmojiItemViewModel? chosen, bool isActive)
        {
            plusText.Visibility = chosen is null ? Visibility.Visible : Visibility.Collapsed;
            emojiText.Visibility = chosen is null ? Visibility.Collapsed : Visibility.Visible;
            clearButton.Visibility = chosen is null ? Visibility.Collapsed : Visibility.Visible;
            if (chosen is not null) emojiText.Text = chosen.Character;

            border.Stroke = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                isActive ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush"];
        }

        // Groups Kitchen-supported emoji into sections by Group only (no subgroup headings) --
        // unlike BuildSections, this groups directly on Group so each group appears exactly once,
        // with every emoji from all of that group's subgroups folded into one list underneath it.
        private static List<EmojiSectionViewModel> BuildKitchenSections(IEnumerable<EmojiModel> emojis)
        {
            return emojis
                .GroupBy(e => e.Group)
                .Select(g => new EmojiSectionViewModel(
                    g.Key,
                    subgroupName: "",
                    isFirstInGroup: true, // one section per group here, so the heading always shows
                    g.Select(e => new Models.EmojiItemViewModel(e)).ToList()))
                .ToList();
        }

        // Kitchen tab's own search box: same match logic as the main tab's UpdateEmojiView, just
        // scoped to SupportedFusionEmojis and swapping between the grouped view and a flat results grid.
        private void OnKitchenSearchTextChanged(object sender, TextChangedEventArgs e) => UpdateKitchenPickerView();

        private void UpdateKitchenPickerView()
        {
            string query = KitchenSearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                KitchenPickerGroupedContent.Visibility = Visibility.Visible;
                KitchenPickerSearchResultsContent.Visibility = Visibility.Collapsed;
                return;
            }

            var matches = (_emojiDataService?.SupportedFusionEmojis ?? Enumerable.Empty<EmojiModel>())
                .Where(emoji =>
                    emoji.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    emoji.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    emoji.Subgroup.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (emoji.Keywords?.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false))
                .Select(e => new Models.EmojiItemViewModel(e))
                .ToList();

            KitchenPickerSearchResultsRepeater.ItemsSource = matches;

            KitchenPickerGroupedContent.Visibility = Visibility.Collapsed;
            KitchenPickerSearchResultsContent.Visibility = Visibility.Visible;
        }

        // TODO: this is UI-only for now. 
        private async Task LoadKitchenMergedResultAsync()
        {
            KitchenResultLoadingRing.IsActive = true;
            KitchenResultImage.Source = null;
            KitchenCopyButton.IsEnabled = false;

            await Task.Delay(400); // stand-in for the real metadata lookup + image fetch

            KitchenResultLoadingRing.IsActive = false;
            KitchenCopyButton.IsEnabled = true;
        }


        private void OnKitchenSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (KitchenResultContent.Visibility == Visibility.Visible)
                _ = LoadKitchenMergedResultAsync();
        }

        // TODO: copy the actual rendered PNG (at KitchenSizeComboBox's selected size)
        // to the clipboard once EmojiKitchenService produces a real bitmap.
        private void OnKitchenCopyClick(object sender, RoutedEventArgs e)
        {
        }

        // ===== Auto-fit window width to exactly N emoji columns =====

        // How many emoji columns the grid should always show. 
        private const int TargetColumns = 6;
        private const double EmojiItemWidth = 48;
        private const double EmojiColumnSpacing = 2;
        // Small safety margin (in DIPs) so a sub-pixel rounding error can't drop
        // us to 5 columns; we land just above the 6-column boundary, not on it.
        private const double ColumnFitBuffer = 2;

        private bool _hasAutoFitColumns;

        private static double TargetAvailableWidth =>
            TargetColumns * EmojiItemWidth + (TargetColumns - 1) * EmojiColumnSpacing + ColumnFitBuffer;

        // Resizes the window (width only, height untouched) so the emoji grid shows
        // exactly TargetColumns columns.
        private async Task AutoFitWindowToColumnsAsync()
        {
            if (_hasAutoFitColumns) return;

            // Wait for a layout pass so section 0 is realized/measured (same pattern
            // WarmUpEmojiGridAsync uses); retry a few times in case layout is slow to settle.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(50);

                if (EmojiRepeater.TryGetElement(0) is FrameworkElement section
                    && section.ActualWidth > 0
                    && this.Content is FrameworkElement root
                    && root.XamlRoot is not null)
                {
                    // Width actually available to the inner UniformGridLayout right now
                    double measuredAvailableWidth = section.ActualWidth;
                    double scale = root.XamlRoot.RasterizationScale; // DIP -> physical px

                    double currentContentWidthDip = root.ActualWidth;
                    double delta = TargetAvailableWidth - measuredAvailableWidth;
                    double newContentWidthDip = currentContentWidthDip + delta;

                    // AppWindow.Resize sets the OUTER frame in physical pixels
                    int currentFramePx = AppWindow.Size.Width;
                    int currentContentPx = (int)Math.Round(currentContentWidthDip * scale);
                    int chromePx = currentFramePx - currentContentPx;

                    int newFramePx = (int)Math.Round(newContentWidthDip * scale) + chromePx;

                    AppWindow.Resize(new Windows.Graphics.SizeInt32(newFramePx, AppWindow.Size.Height));

                    if (AppWindow.Presenter is OverlappedPresenter presenter)
                    {
                        presenter.PreferredMinimumWidth = newFramePx;
                        presenter.PreferredMaximumWidth = newFramePx;
                        presenter.PreferredMinimumHeight = newFramePx;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"AutoFitWindowToColumns: measuredAvailableWidth={measuredAvailableWidth:F1} " +
                        $"scale={scale} target={TargetAvailableWidth:F1} newFramePx={newFramePx}");

                    _hasAutoFitColumns = true;
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine("AutoFitWindowToColumns: gave up — section 0 never realized.");
        }

        // ===== Emoji grid: data + hover visuals =====

        public async Task SetEmojiData(EmojiDataService emojiDataService)
        {
            // Keep "Loading emoji..." visible & EmojiTabContent transparent to allow for glyphs to precache
            EmojiTabContent.Visibility = Visibility.Visible;

            _emojiDataService = emojiDataService;
            _emojiByCharacter = _emojiDataService.Emojis.ToDictionary(e => e.Character);

            RefreshGroupedView();
            await AutoFitWindowToColumnsAsync(); // resize BEFORE warmup so precache-scroll happens at final size

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

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(AppWindow.Size.Width, 480));
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
            System.Diagnostics.Debug.Write(EmojiTabContent.ExtentHeight);
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
            var scrollOptions = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled);
            // Let one layout pass happen so ScrollableHeight is actually valid.
            await Task.Delay(50);

            double viewportHeight = EmojiTabContent.ViewportHeight;
            double maxOffset = EmojiTabContent.ScrollableHeight;

            if (viewportHeight <= 0 || maxOffset <= 0)
                return; // nothing to warm up (e.g. window not yet sized)

            int totalProgressBarTicks = (int)Math.Ceiling(maxOffset / viewportHeight);
            LoadingProgressBar.Value = 0;

            // Step through the full scrollable range in viewport-sized increments to precache everything.
            // Cannot immediately scroll from very top to very bottom since the glyphs in the middle would not get cached.
            for (int tick = 1; tick <= totalProgressBarTicks; tick++)
            {
                double offset = Math.Min(tick * viewportHeight, maxOffset);
                await ScrollEmojiTabToAsync(EmojiTabContent.HorizontalOffset, offset, scrollOptions);
                // Give the newly visible emojis a chance to render before moving on.
                await Task.Delay(20);

                // Save the last 5% for returning to the top and settling.
                LoadingProgressBar.Value = 95.0 * tick / totalProgressBarTicks;
            }

            await ScrollEmojiTabToAsync(0, 0, scrollOptions);
            await Task.Delay(350);
        }

        private async Task ScrollEmojiTabToAsync(double horizontalOffset, double verticalOffset, ScrollingScrollOptions options)
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int correlationId = -1;

            void OnScrollCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
            {
                if (args.CorrelationId == correlationId)
                    completed.TrySetResult(true);
            }

            EmojiTabContent.ScrollCompleted += OnScrollCompleted;
            try
            {
                correlationId = EmojiTabContent.ScrollTo(horizontalOffset, verticalOffset, options);
                // -1 means the control did not start a scroll (for example, already at the target).
                if (correlationId != -1)
                    await completed.Task;
            }
            finally
            {
                EmojiTabContent.ScrollCompleted -= OnScrollCompleted;
            }
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

        // Show section headings without a search query, and no headings (flat grid) with query.
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

            //System.Diagnostics.Debug.WriteLine("cur scroll: " + EmojiTabContent.VerticalOffset);
            //System.Diagnostics.Debug.Write("total height: " + EmojiTabContent.ExtentHeight);

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
