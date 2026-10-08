using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Services;

namespace FluentEmojiKitchen.Presenters;

public sealed class KitchenPresenter : IDisposable
{
    private readonly IKitchenView _view;
    private readonly EmojiQueryService _queries;
    private readonly IKitchenService _service;
    private IEmojiCatalog? _catalog;
    private KitchenSlot _active;
    private EmojiItem? _left;
    private EmojiItem? _right;
    private int _size = 48;
    private bool _loading;
    private bool _disposed;
    private KitchenResult? _result;
    private CancellationTokenSource? _request;

    public KitchenPresenter(IKitchenView view, EmojiQueryService queries, IKitchenService service)
    {
        _view = view;
        _queries = queries;
        _service = service;
        _view.SlotTapped += OnSlotTapped;
        _view.SlotCleared += OnSlotCleared;
        _view.EmojiChosen += OnEmojiChosen;
        _view.SearchChanged += OnSearchChanged;
        _view.SizeChangedRequested += OnSizeChanged;
        _view.CopyRequested += OnCopyRequested;
    }

    public void Initialize(IEmojiCatalog catalog)
    {
        _catalog = catalog;
        _view.SetSections(_queries.Group(catalog.SupportedFusionEmojis, false));
        OnSearchChanged("");
        Render();
    }

    public void ResetSearch() => _view.ResetSearch();

    private void OnSlotTapped(KitchenSlot slot)
    {
        if (slot == KitchenSlot.None) return;
        _active = _active == slot ? KitchenSlot.None : slot;
        Refresh();
    }

    private void OnSlotCleared(KitchenSlot slot)
    {
        if (slot == KitchenSlot.Left) _left = null;
        else if (slot == KitchenSlot.Right) _right = null;
        Refresh();
    }

    private void OnEmojiChosen(EmojiItem item)
    {
        if (_active == KitchenSlot.None) return;
        if (_active == KitchenSlot.Left) _left = item;
        else _right = item;
        _active = KitchenSlot.None;
        Refresh();
    }

    private void OnSearchChanged(string query)
    {
        bool searching = !string.IsNullOrWhiteSpace(query);
        var results = searching && _catalog is not null
            ? _queries.Search(_catalog.SupportedFusionEmojis, query) : new List<EmojiItem>();
        _view.ShowSearchResults(results, searching);
    }

    private void OnSizeChanged(int size)
    {
        _size = size;
        Refresh();
    }

    private void Refresh()
    {
        _request?.Cancel();
        _result = null;
        _loading = false;
        if (_active == KitchenSlot.None && _left is not null && _right is not null)
            _ = LoadResultAsync(_left.Model, _right.Model);
        else Render();
    }

    private async Task LoadResultAsync(EmojiModel left, EmojiModel right)
    {
        using var request = new CancellationTokenSource();
        _request = request;
        _loading = true;
        Render();
        try
        {
            var result = await _service.CreateAsync(left, right, _size, request.Token);
            if (request.IsCancellationRequested || _disposed) return;
            _result = result;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (!request.IsCancellationRequested && !_disposed)
                _result = new KitchenResult(null, "Could not load this fusion.");
        }
        finally
        {
            // A previous request must never overwrite a newer selection or size.
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                _loading = false;
                if (!_disposed) Render();
            }
        }
    }

    private async void OnCopyRequested()
    {
        if (_result?.ImageUri is null) return;
        try { await _service.CopyAsync(_result, CancellationToken.None); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void Render() => _view.Render(new KitchenViewState(_active, _left, _right, _loading, _result));

    public void Dispose()
    {
        _disposed = true;
        _request?.Cancel();
        _view.SlotTapped -= OnSlotTapped;
        _view.SlotCleared -= OnSlotCleared;
        _view.EmojiChosen -= OnEmojiChosen;
        _view.SearchChanged -= OnSearchChanged;
        _view.SizeChangedRequested -= OnSizeChanged;
        _view.CopyRequested -= OnCopyRequested;
    }
}
