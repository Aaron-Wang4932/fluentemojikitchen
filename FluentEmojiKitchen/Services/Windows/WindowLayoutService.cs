using System;
using System.Threading.Tasks;
using FluentEmojiKitchen.Configuration;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
namespace FluentEmojiKitchen.Services.Windows;

public sealed class WindowLayoutService : IWindowLayout
{
    private readonly Window _window;
    private readonly EmojiPanelView _view;
    private readonly AppOptions _options;
    public WindowLayoutService(Window window, EmojiPanelView view, AppOptions options)
    {
        _window = window;
        _view = view;
        _options = options;
    }
    public void FinishLoading()
    {
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsResizable = true;
        _window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(_window.AppWindow.Size.Width, _options.WindowHeight));
    }
    private int TargetColumns => _options.TargetColumns;
    private const double EmojiItemWidth = 48;
    private const double EmojiColumnSpacing = 2;
    // Small safety margin (in DIPs) so a sub-pixel rounding error can't drop
    // us to 5 columns; we land just above the 6-column boundary, not on it.
    private const double ColumnFitBuffer = 2;

    private bool _hasAutoFitColumns;

    private double TargetAvailableWidth =>
        TargetColumns * EmojiItemWidth + (TargetColumns - 1) * EmojiColumnSpacing + ColumnFitBuffer;

    // Resizes the window (width only, height untouched) so the emoji grid shows
    // exactly TargetColumns columns.
    public async Task FitColumnsAsync()
    {
        if (_hasAutoFitColumns) return;

        // Wait for a layout pass so section 0 is realized/measured (same pattern
        // WarmUpEmojiGridAsync uses); retry a few times in case layout is slow to settle.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(50);

            if (_view.GroupedRepeater.TryGetElement(0) is FrameworkElement section
                && section.ActualWidth > 0
                && _window.Content is FrameworkElement root
                && root.XamlRoot is not null)
            {
                // Width actually available to the inner UniformGridLayout right now
                double measuredAvailableWidth = section.ActualWidth;
                double scale = root.XamlRoot.RasterizationScale; // DIP -> physical px

                double currentContentWidthDip = root.ActualWidth;
                double delta = TargetAvailableWidth - measuredAvailableWidth;
                double newContentWidthDip = currentContentWidthDip + delta;

                // _window.AppWindow.Resize sets the OUTER frame in physical pixels
                int currentFramePx = _window.AppWindow.Size.Width;
                int currentContentPx = (int)Math.Round(currentContentWidthDip * scale);
                int chromePx = currentFramePx - currentContentPx;

                int newFramePx = (int)Math.Round(newContentWidthDip * scale) + chromePx;

                _window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(newFramePx, _window.AppWindow.Size.Height));

                if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
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

}
