using System;
using System.Collections.Generic;
using System.Linq;
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
    private IKitchenCompatibility? _compatibility;
    private IReadOnlyList<EmojiModel> _supported = Array.Empty<EmojiModel>();
    private List<EmojiSection> _sections = new();
    private IReadOnlyList<EmojiItem> _searchResults = Array.Empty<EmojiItem>();
    private KitchenSlot _active;
    private EmojiItem? _left;
    private EmojiItem? _right;
    private int _size = 48;
    private bool _loading;
    private bool _disposed, _copying;
    private readonly CancellationTokenSource _lifetime = new();
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

    public void Initialize(IEmojiCatalog catalog, IKitchenCompatibility compatibility)
    {
        _catalog = catalog;
        _compatibility = compatibility;
        _supported = catalog.Emojis.Where(compatibility.Supports).ToList();
        _sections = _queries.Group(_supported, false);
        _view.SetSections(_sections);
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
        if (!CanChoose(item)) return;
        if (_active == KitchenSlot.Left) _left = item;
        else _right = item;
        _active = KitchenSlot.None;
        Refresh();
    }

    private void OnSearchChanged(string query)
    {
        bool searching = !string.IsNullOrWhiteSpace(query);
        var results = searching && _catalog is not null
            ? _queries.Search(_supported, query) : new List<EmojiItem>();
        _searchResults = results;
        UpdateAvailability();
        _view.ShowSearchResults(results, searching);
    }

    private void OnSizeChanged(int size)
    {
        if (size is not (48 or 128 or 256 or 512)) return;
        _size = size;
        if (_result is not null) _result = _result with { CopySize = size, Message = null };
        Render(); // No download, source replacement, preview resize, or request cancellation.
    }

    private void Refresh()
    {
        _request?.Cancel();
        _request = null; // Old completions cannot render after a slot is opened or cleared.
        UpdateAvailability();
        _result = null;
        _loading = false;
        if (_active == KitchenSlot.None && _left is not null && _right is not null)
            _ = LoadResultAsync(_left.Model, _right.Model);
        else Render();
    }

    private bool CanChoose(EmojiItem candidate)
    {
        if (_active == KitchenSlot.None || _compatibility is null) return false;
        var other = _active == KitchenSlot.Left ? _right : _left;
        return _compatibility.Supports(candidate.Model) &&
            (other is null || _compatibility.CanCombine(candidate.Model, other.Model));
    }

    private void UpdateAvailability()
    {
        foreach (var item in _sections.SelectMany(s => s.Emojis)) item.IsEnabled = CanChoose(item);
        foreach (var item in _searchResults) item.IsEnabled = CanChoose(item);
    }

    private async Task LoadResultAsync(EmojiModel left, EmojiModel right)
    {
        if (_compatibility is null || !_compatibility.CanCombine(left, right))
        {
            _result = new KitchenResult(null, "These emojis cannot be combined.");
            Render();
            return;
        }
        using var request = new CancellationTokenSource();
        _request = request;
        _loading = true;
        Render();
        try
        {
            var result = await _service.CreateAsync(left, right, _size, request.Token);
            if (request.IsCancellationRequested || _disposed) return;
            _result = result with { CopySize = _size };
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
        if (_copying || _disposed || _result is not { CanCopy: true, ImageUri: not null }) return;
        var displayed = _result;
        var snapshot = displayed with { CopySize = _size };
        _copying = true;
        Render();
        try
        {
            await _service.CopyAsync(snapshot, _lifetime.Token);
            if (!_disposed && ReferenceEquals(_result, displayed))
                _result = displayed with { Message = $"Copied {snapshot.CopySize} × {snapshot.CopySize} PNG." };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (!_disposed && ReferenceEquals(_result, displayed))
                _result = displayed with { Message = "Could not copy the image. Please try again." };
        }
        finally
        {
            _copying = false;
            if (!_disposed) Render();
        }
    }

    private void Render() => _view.Render(new KitchenViewState(_active, _left, _right, _loading, _result, _copying));

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _request?.Cancel();
        _view.SlotTapped -= OnSlotTapped;
        _view.SlotCleared -= OnSlotCleared;
        _view.EmojiChosen -= OnEmojiChosen;
        _view.SearchChanged -= OnSearchChanged;
        _view.SizeChangedRequested -= OnSizeChanged;
        _view.CopyRequested -= OnCopyRequested;
    }
}
