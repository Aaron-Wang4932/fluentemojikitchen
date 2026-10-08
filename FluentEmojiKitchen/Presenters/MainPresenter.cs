using System;
using System.Threading.Tasks;
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
    private readonly Func<Task<IEmojiCatalog>> _loadCatalog;
    private readonly AppOptions _options;

    public MainPresenter(IMainView view, IPopupWindow popup, IWindowLayout layout,
        EmojiPanelPresenter emoji, KitchenPresenter kitchen,
        Func<Task<IEmojiCatalog>> loadCatalog, AppOptions options)
    {
        _view = view;
        _popup = popup;
        _layout = layout;
        _emoji = emoji;
        _kitchen = kitchen;
        _loadCatalog = loadCatalog;
        _options = options;
        _view.TabRequested += OnTabRequested;
        _view.CloseRequested += _popup.Hide;
        _popup.Hidden += OnHidden;
    }

    public async Task InitializeAsync()
    {
        _view.SetLoading(true, "Loading emojis...", 0);
        try
        {
            var catalog = await _loadCatalog();
            _emoji.Initialize(catalog);
            _kitchen.Initialize(catalog);
            await _layout.FitColumnsAsync();
            if (_options.PrecacheEmojis)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await _emoji.WarmUpAsync(p => _view.SetLoading(true, "Pre-caching emojis...", p));
                System.Diagnostics.Debug.WriteLine($"Warmup took {timer.ElapsedMilliseconds}ms");
            }
            _emoji.SetReady();
            _layout.FinishLoading();
            _view.SetLoading(false, "Ready", 100);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emoji initialization failed: {ex}");
            _view.ShowInitializationError("Could not load emoji data. Check your connection and restart the app.");
        }
    }

    public void TogglePopup() => _popup.Toggle();
    private void OnTabRequested(PanelTab tab) => _view.ShowTab(tab);
    private void OnHidden()
    {
        _emoji.Reset();
        _kitchen.ResetSearch();
    }

    public void Dispose()
    {
        _view.TabRequested -= OnTabRequested;
        _view.CloseRequested -= _popup.Hide;
        _popup.Hidden -= OnHidden;
        _emoji.Dispose();
        _kitchen.Dispose();
    }
}
