using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Views.Behaviors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace FluentEmojiKitchen.Views;

public sealed partial class EmojiPanelView : UserControl, IEmojiPanelView
{
    public event Action<string>? SearchChanged;
    public event Action<EmojiItem>? EmojiChosen;
    internal ItemsRepeater GroupedRepeater => EmojiRepeater;
    public event Action? SearchFocusRequested;
    public EmojiPanelView()
    {
        InitializeComponent();
        EmojiPanelSearchBox.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => SearchFocusRequested?.Invoke()), true);
    }
    public void SetSections(IEnumerable<EmojiSection> sections) => EmojiRepeater.ItemsSource = sections;
    public void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching)
    {
        SearchResultsRepeater.ItemsSource = results;
        EmojiTabContent.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchResultsContent.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        ScrollToTopButton.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
    }
    public void SetReady()
    {
        EmojiPanelSearchBox.Visibility = Visibility.Visible;
        OnEmojiTabContentScrolled(this, EventArgs.Empty);
    }
    public void ResetView()
    {
        EmojiPanelSearchBox.Text = "";
        EmojiTabContent.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }
    public void FocusSearch() => EmojiPanelSearchBox.Focus(FocusState.Programmatic);
    public void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
    public Task WarmUpAsync(Action<double> reportProgress) => new ScrollWarmupBehavior(EmojiTabContent).RunAsync(reportProgress);
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => SearchChanged?.Invoke(EmojiPanelSearchBox.Text);
    private void OnEmojiItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EmojiItem item })
            EmojiChosen?.Invoke(item);
    }

    private void OnEmojiDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EmojiItem item })
            EmojiChosen?.Invoke(item);
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

    private void OnEmojiTabContentScrolled(object sender, object e)
    {
        bool show = EmojiTabContent.VerticalOffset > 200;
        ScrollToTopButton.Opacity = show ? 1 : 0;
        ScrollToTopButton.IsHitTestVisible = show;
    }
#if DEBUG
    // Minimal timing: no observation timer, rendering callback, or layout trace.
    private void OnScrollToTopClick(object sender, RoutedEventArgs e)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        double from = EmojiTabContent.VerticalOffset;
        int correlationId = -1;

        void OnCompleted(ScrollView view, ScrollingScrollCompletedEventArgs args)
        {
            if (args.CorrelationId != correlationId) return;
            EmojiTabContent.ScrollCompleted -= OnCompleted;
            System.Diagnostics.Debug.WriteLine(
                $"[TopTiming] from={from:F1}, to={EmojiTabContent.VerticalOffset:F1}, " +
                $"elapsedMs={timer.ElapsedMilliseconds}, correlationId={correlationId}");
        }

        EmojiTabContent.ScrollCompleted += OnCompleted;
        try
        {
            correlationId = EmojiTabContent.ScrollTo(EmojiTabContent.HorizontalOffset, 0,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            if (correlationId == -1) EmojiTabContent.ScrollCompleted -= OnCompleted;
        }
        catch
        {
            EmojiTabContent.ScrollCompleted -= OnCompleted;
            throw;
        }
    }
#else
    private void OnScrollToTopClick(object sender, RoutedEventArgs e) =>
        EmojiTabContent.ScrollTo(EmojiTabContent.HorizontalOffset, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
#endif
}
