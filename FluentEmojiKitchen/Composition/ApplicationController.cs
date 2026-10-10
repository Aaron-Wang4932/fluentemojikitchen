using System;
using System.Threading.Tasks;
using FluentEmojiKitchen.Configuration;
using FluentEmojiKitchen.Listeners;
using FluentEmojiKitchen.Presenters;
using FluentEmojiKitchen.Services;
using FluentEmojiKitchen.Services.Windows;
using Microsoft.UI.Xaml;
namespace FluentEmojiKitchen.Composition;

// Composition root: the only place that constructs the concrete object graph.
public sealed class ApplicationController : IDisposable
{
    private readonly MainWindow _window;
    private readonly PopupWindowService _popup;
    private readonly MainPresenter _presenter;
    private readonly ApplicationInputListener _input;
    private readonly EmojiInsertionQueue _insertion;
    private readonly OnlineKitchenService _images;
    private readonly AppOptions _options;
    private readonly ColumnSettingsStore _settings = new(CacheFiles.DirectoryPath);
    private readonly WindowLayoutService _layout;
    private bool _changingColumns;
    private bool _disposed;
    public ApplicationController(AppOptions options)
    {
        _options = options;
        options.TargetColumns = _settings.Load(options.TargetColumns);
        _window = new MainWindow();
        _window.SetColumns(options.TargetColumns);
        _popup = new PopupWindowService(_window);
        _layout = new WindowLayoutService(_window, _window.EmojiView, options);
        var queries = new EmojiQueryService();
        _insertion = new EmojiInsertionQueue(_popup);
        _window.EmojiView.SearchFocusRequested += BeginEmojiSearch;
        _window.KitchenView.SearchFocusRequested += BeginKitchenSearch;
        var emoji = new EmojiPanelPresenter(_window.EmojiView, queries, _insertion);
        _images = new OnlineKitchenService(System.IO.Path.Combine(CacheFiles.DirectoryPath, "kitchen-images"), new KitchenImageClipboard(WinRT.Interop.WindowNative.GetWindowHandle(_window)));
        var metadata = new KitchenMetadataCache(CacheFiles.DirectoryPath, new KitchenMetadataParser());
        var loader = new StartupDataLoader(metadata, _images, () => new HotkeyRegistryService().RebuildPathCache());
        var kitchen = new KitchenPresenter(_window.KitchenView, queries, _images);
        _presenter = new MainPresenter(_window, _popup, _layout, emoji, kitchen,
            loader, options);
        _window.SettingsFocusRequested += BeginSettings;
        _window.ColumnsRequested += ApplyColumns;
        _window.RebuildRequested += Rebuild;
        _popup.Hidden += _window.HideSettings;
        _input = new ApplicationInputListener(_presenter.TogglePopup, Exit);
    }
    private void BeginSettings() => _popup.BeginSearch(_window.FocusSettings);
    private async void ApplyColumns(int columns)
    {
        if (_disposed || _presenter.IsBusy || _changingColumns) return;
        if (columns < AppOptions.MinimumColumns || columns > AppOptions.MaximumColumns) return;
        _changingColumns = true;
        _window.SetSettingsEnabled(false);
        var prior = _window.BeginColumnLayout();
        try
        {
            _options.TargetColumns = columns;
            _layout.ResetFit();
            await _layout.FitColumnsAsync();
            if (_disposed) return;
            _settings.Save(_options.TargetColumns);
            _window.SetColumns(_options.TargetColumns);
            _window.ShowSettingsStatus($"Saved {_options.TargetColumns} columns.");
            _window.HideSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (!_disposed) _window.ShowSettingsStatus("Could not apply or save columns. Try again.");
        }
        finally
        {
            _changingColumns = false;
            if (!_disposed) { _window.EndColumnLayout(prior); _window.SetSettingsEnabled(true); }
        }
    }
    private async void Rebuild()
    {
        if (_disposed || _changingColumns || _presenter.IsBusy) return;
        _window.ShowTab(Presentation.PanelTab.Emoji);
        await _presenter.RebuildCachesAsync();
        if (!_disposed) _window.SetColumns(_options.TargetColumns);
    }
    private void BeginEmojiSearch() => _popup.BeginSearch(_window.EmojiView.FocusSearch);
    private void BeginKitchenSearch() => _popup.BeginSearch(_window.KitchenView.FocusSearch);
    public Task StartAsync()
    {
        _input.Start();
        return _presenter.InitializeAsync();
    }
    private void Exit()
    {
        Dispose();
        Application.Current.Exit();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Dispose();
        _window.SettingsFocusRequested -= BeginSettings;
        _window.ColumnsRequested -= ApplyColumns;
        _window.RebuildRequested -= Rebuild;
        _popup.Hidden -= _window.HideSettings;
        _window.EmojiView.SearchFocusRequested -= BeginEmojiSearch;
        _window.KitchenView.SearchFocusRequested -= BeginKitchenSearch;
        _insertion.Dispose();
        _presenter.Dispose();
        _images.Dispose();
        _popup.Dispose();
    }
}
