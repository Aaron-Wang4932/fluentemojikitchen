using System;
using System.Threading.Tasks;
using System.Threading;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Configuration;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Presentation;

namespace FluentEmojiKitchen.Presenters;

public sealed class MainPresenter : IDisposable
{
    private readonly IMainView _view;
    private readonly IPopupWindow _popup;
    private readonly IWindowLayout _layout;
    private readonly EmojiPanelPresenter _emoji;
    private readonly KitchenPresenter _kitchen;
    private readonly IStartupDataLoader _loader;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed, _initializing, _ready;
    private double _progress;
    private readonly TaskCompletionSource _firstShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AppOptions _options;

    public MainPresenter(IMainView view, IPopupWindow popup, IWindowLayout layout,
        EmojiPanelPresenter emoji, KitchenPresenter kitchen,
        IStartupDataLoader loader, AppOptions options)
    {
        _view = view;
        _popup = popup;
        _layout = layout;
        _emoji = emoji;
        _kitchen = kitchen;
        _loader = loader;
        _options = options;
        _view.TabRequested += OnTabRequested;
        _view.CloseRequested += _popup.Hide;
        _popup.Hidden += OnHidden;
        _popup.Shown += OnShown;
    }

    public async Task InitializeAsync()
    {
        _initializing = true;
        _view.SetLoading(true, "Preparing emoji data...", 0);
        var progress = new Progress<StartupProgress>(p =>
        {
            if (!_initializing || _disposed) return;
            _progress = Math.Max(_progress, Math.Clamp(p.Percent, 0, 99));
            _view.SetLoading(true, p.Message, _progress);
        });
        try
        {
            var data = await _loader.LoadAsync(progress, _lifetime.Token);
            if (_disposed) return;
            _initializing = false; // Ignore late queued download reports during UI setup.
            _view.SetLoading(true, "Building emoji panels...", 86);
            _emoji.Initialize(data.Catalog);
            _kitchen.Initialize(data.Catalog, data.Compatibility);
            // Realize layout only after the user opens the otherwise hidden startup window.
            await _firstShown.Task.WaitAsync(_lifetime.Token);
            await _layout.FitColumnsAsync();
            if (_disposed) return;
            _view.SetLoading(true, "Preparing layout...", 90);
            if (_options.PrecacheEmojis)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await _emoji.WarmUpAsync(p => { if (!_disposed) _view.SetLoading(true, "Pre-caching emojis...", 90 + p * .09); });
                System.Diagnostics.Debug.WriteLine($"Warmup took {timer.ElapsedMilliseconds}ms");
            }
            if (_disposed) return;
            _emoji.SetReady();
            _layout.FinishLoading();
            _ready = true;
            _view.SetLoading(false, "Ready", 100);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emoji initialization failed: {ex}");
            _initializing = false;
            if (_disposed) return;
            _view.ShowInitializationError("Could not load emoji data. Check your connection and restart the app.");
        }
    }

    private void OnShown() => _firstShown.TrySetResult();
    public void TogglePopup() => _popup.Toggle();
    private void OnTabRequested(PanelTab tab) { if (_ready) _view.ShowTab(tab); }
    private void OnHidden()
    {
        _emoji.Reset();
        _kitchen.ResetSearch();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _view.TabRequested -= OnTabRequested;
        _view.CloseRequested -= _popup.Hide;
        _popup.Hidden -= OnHidden;
        _popup.Shown -= OnShown;
        _emoji.Dispose();
        _kitchen.Dispose();
    }
}
