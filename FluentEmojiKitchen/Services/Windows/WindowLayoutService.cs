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
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsResizable = true;

        if (_window.Content is not FrameworkElement root ||
            root.XamlRoot is null)
            return;

        double scale = root.XamlRoot.RasterizationScale;
        var appWindow = _window.AppWindow;

        int chromeHeight = Math.Max(
            0,
            appWindow.Size.Height - (int)Math.Round(root.ActualHeight * scale));

        var workArea = DisplayArea.GetFromWindowId(
            appWindow.Id,
            DisplayAreaFallback.Nearest).WorkArea;

        int height = Math.Min(
            (int)Math.Ceiling(_options.WindowHeight * scale) + chromeHeight,
            workArea.Height);

        appWindow.Resize(new global::Windows.Graphics.SizeInt32(
            appWindow.Size.Width, height));

        // Keep the enlarged window within the monitor vertically.
        int y = Math.Clamp(
            appWindow.Position.Y,
            workArea.Y,
            workArea.Y + workArea.Height - height);

        appWindow.Move(new global::Windows.Graphics.PointInt32(
            appWindow.Position.X, y));
    }
    private int TargetColumns => _options.TargetColumns;
    private const double EmojiItemWidth = 48;
    private const double EmojiColumnSpacing = 2;
    // Small safety margin (in DIPs) so a sub-pixel rounding error can't drop
    // us to 5 columns; we land just above the 6-column boundary, not on it.
    private const double ColumnFitBuffer = 2;

    private bool _hasAutoFitColumns;
    public void ResetFit() => _hasAutoFitColumns = false;

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

                var display = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Nearest);
                if (newFramePx > display.WorkArea.Width)
                {
                    int fit = (int)Math.Floor((TargetAvailableWidth - (newFramePx - display.WorkArea.Width) / scale + EmojiColumnSpacing - ColumnFitBuffer) / (EmojiItemWidth + EmojiColumnSpacing));
                    if (fit >= AppOptions.MinimumColumns && fit < _options.TargetColumns)
                    { _options.TargetColumns = fit; continue; }
                }
                // Remove the old width clamp before applying a different column count.
                if (_window.AppWindow.Presenter is OverlappedPresenter previous)
                { previous.PreferredMinimumWidth = 0; previous.PreferredMaximumWidth = 0; }
                _window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(newFramePx, _window.AppWindow.Size.Height));

                if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = newFramePx;
                    presenter.PreferredMaximumWidth = newFramePx;
                    presenter.PreferredMinimumHeight = Math.Min(newFramePx, _options.WindowHeight);
                }

                System.Diagnostics.Debug.WriteLine(
                    $"AutoFitWindowToColumns: measuredAvailableWidth={measuredAvailableWidth:F1} " +
                    $"scale={scale} target={TargetAvailableWidth:F1} newFramePx={newFramePx}");

                _hasAutoFitColumns = true;
                return;
            }
        }

        throw new InvalidOperationException("Could not measure the emoji grid for column resizing.");
    }

}
