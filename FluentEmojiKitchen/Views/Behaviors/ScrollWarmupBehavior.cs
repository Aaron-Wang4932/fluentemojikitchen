using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
namespace FluentEmojiKitchen.Views.Behaviors;

// Rendering/scrolling is a view concern. This preserves the uploaded scroll-through warmup.
internal sealed class ScrollWarmupBehavior
{
    private readonly ScrollView _scrollView;
    public ScrollWarmupBehavior(ScrollView scrollView) => _scrollView = scrollView;
    public async Task RunAsync(Action<double> reportProgress)
    {
        var scrollOptions = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled);
        // Let one layout pass happen so ScrollableHeight is actually valid.
        await Task.Delay(50);

        double viewportHeight = _scrollView.ViewportHeight;
        double maxOffset = _scrollView.ScrollableHeight;

        if (viewportHeight <= 0 || maxOffset <= 0)
            return; // nothing to warm up (e.g. window not yet sized)

        int totalProgressBarTicks = (int)Math.Ceiling(maxOffset / viewportHeight);
        reportProgress(0);

        // Step through the full scrollable range in viewport-sized increments to precache everything.
        // Cannot immediately scroll from very top to very bottom since the glyphs in the middle would not get cached.
        for (int tick = 1; tick <= totalProgressBarTicks; tick++)
        {
            double offset = Math.Min(tick * viewportHeight, maxOffset);
            await ScrollEmojiTabToAsync(_scrollView.HorizontalOffset, offset, scrollOptions);
            // Give the newly visible emojis a chance to render before moving on.
            await Task.Delay(20);

            // Save the last 5% for returning to the top and settling.
            reportProgress(95.0 * tick / totalProgressBarTicks);
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

        _scrollView.ScrollCompleted += OnScrollCompleted;
        try
        {
            correlationId = _scrollView.ScrollTo(horizontalOffset, verticalOffset, options);
            // -1 means the control did not start a scroll (for example, already at the target).
            if (correlationId != -1)
                await completed.Task;
        }
        finally
        {
            _scrollView.ScrollCompleted -= OnScrollCompleted;
        }
    }



}
