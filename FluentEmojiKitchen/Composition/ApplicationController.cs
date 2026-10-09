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
    private bool _disposed;
    public ApplicationController(AppOptions options)
    {
        _window = new MainWindow();
        _popup = new PopupWindowService(_window);
        var layout = new WindowLayoutService(_window, _window.EmojiView, options);
        var queries = new EmojiQueryService();
        _insertion = new EmojiInsertionQueue(_popup);
        _window.EmojiView.SearchFocusRequested += BeginEmojiSearch;
        _window.KitchenView.SearchFocusRequested += BeginKitchenSearch;
        var emoji = new EmojiPanelPresenter(_window.EmojiView, queries, _insertion);
        _images = new OnlineKitchenService(System.IO.Path.Combine(CacheFiles.DirectoryPath, "kitchen-images"), new KitchenImageClipboard(WinRT.Interop.WindowNative.GetWindowHandle(_window)));
        var metadata = new KitchenMetadataCache(CacheFiles.DirectoryPath, new KitchenMetadataParser());
        var loader = new StartupDataLoader(metadata, _images);
        var kitchen = new KitchenPresenter(_window.KitchenView, queries, _images);
        _presenter = new MainPresenter(_window, _popup, layout, emoji, kitchen,
            loader, options);
        _input = new ApplicationInputListener(_presenter.TogglePopup, Exit);
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
        _window.EmojiView.SearchFocusRequested -= BeginEmojiSearch;
        _window.KitchenView.SearchFocusRequested -= BeginKitchenSearch;
        _insertion.Dispose();
        _presenter.Dispose();
        _images.Dispose();
        _popup.Dispose();
    }
}
