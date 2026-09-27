using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace FluentEmojiKitchen.Helpers
{
    public sealed class EmojiSectionStackLayout : NonVirtualizingLayout
    {
        // Matches the existing outer StackLayout spacing.
        private const double SectionSpacing = 4;

        protected override Size MeasureOverride(
            NonVirtualizingLayoutContext context,
            Size availableSize)
        {
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

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(
            NonVirtualizingLayoutContext context,
            Size finalSize)
        {
            double y = 0;

            foreach (var child in context.Children)
            {
                double height = child.DesiredSize.Height;

                child.Arrange(new Rect(
                    0, y, finalSize.Width, height));

                y += height + SectionSpacing;
            }

            return finalSize;
        }
    }
}