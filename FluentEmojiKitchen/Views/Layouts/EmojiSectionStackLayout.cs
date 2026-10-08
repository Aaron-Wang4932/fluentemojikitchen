using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using Windows.Foundation;

namespace FluentEmojiKitchen.Views.Layouts
{
    public sealed class EmojiSectionStackLayout : NonVirtualizingLayout
    {
        // Temporary, aggregated instrumentation. No per-child debugger output.
        private static bool _tracing;
        private static int _measureCount, _arrangeCount;
        private static double _measureMs, _arrangeMs, _maxMeasureMs, _maxArrangeMs;

        internal static void BeginTrace()
        {
            _measureCount = _arrangeCount = 0;
            _measureMs = _arrangeMs = _maxMeasureMs = _maxArrangeMs = 0;
            _tracing = true;
        }

        internal static string EndTrace()
        {
            _tracing = false;
            return $"[TopTrace] sectionLayout measureCalls={_measureCount}, " +
                $"measureTotalMs={_measureMs:F1}, measureMaxMs={_maxMeasureMs:F1}, " +
                $"arrangeCalls={_arrangeCount}, arrangeTotalMs={_arrangeMs:F1}, " +
                $"arrangeMaxMs={_maxArrangeMs:F1}";
        }

        // Matches the existing outer StackLayout spacing.
        private const double SectionSpacing = 4;

        protected override Size MeasureOverride(
            NonVirtualizingLayoutContext context,
            Size availableSize)
        {
            long start = _tracing ? Stopwatch.GetTimestamp() : 0;
            double width = 0;
            double height = 0;
            int count = 0;

            foreach (var child in context.Children)
            {
                child.Measure(new Size(
                    availableSize.Width,
                    double.PositiveInfinity));

                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
                count++;
            }

            if (count > 1)
                height += SectionSpacing * (count - 1);

            if (_tracing)
            {
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                _measureCount++;
                _measureMs += elapsed;
                _maxMeasureMs = Math.Max(_maxMeasureMs, elapsed);
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(
            NonVirtualizingLayoutContext context,
            Size finalSize)
        {
            long start = _tracing ? Stopwatch.GetTimestamp() : 0;
            double y = 0;

            foreach (var child in context.Children)
            {
                double height = child.DesiredSize.Height;

                child.Arrange(new Rect(
                    0, y, finalSize.Width, height));

                y += height + SectionSpacing;
            }

            if (_tracing)
            {
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                _arrangeCount++;
                _arrangeMs += elapsed;
                _maxArrangeMs = Math.Max(_maxArrangeMs, elapsed);
            }
            return finalSize;
        }
    }
}
