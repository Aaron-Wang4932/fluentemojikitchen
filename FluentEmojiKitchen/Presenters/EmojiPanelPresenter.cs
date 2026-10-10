using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Services;

namespace FluentEmojiKitchen.Presenters;

public sealed class EmojiPanelPresenter : IDisposable
{
    private readonly IEmojiPanelView _view;
    private readonly EmojiQueryService _queries;
    private readonly IEmojiInsertionQueue _insertion;
    private readonly ObservableCollection<EmojiSection> _sections = new();
    private IEmojiCatalog? _catalog;
    private Dictionary<string, EmojiModel> _byCharacter = new();
    private EmojiItem? _selected;

    public EmojiPanelPresenter(IEmojiPanelView view, EmojiQueryService queries, IEmojiInsertionQueue insertion)
    {
        _view = view;
        _queries = queries;
        _insertion = insertion;
        _view.SearchChanged += OnSearchChanged;
        _view.EmojiChosen += OnEmojiChosen;
    }

    public void Initialize(IEmojiCatalog catalog)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = null;
        _catalog = catalog;
        _byCharacter = catalog.Emojis.ToDictionary(e => e.Character);
        // Detach once before rebuilding: avoid thousands of incremental layout updates.
        _view.SetSections(Array.Empty<EmojiSection>());
        _sections.Clear();
        var recent = BuildRecentSection();
        if (recent is not null) _sections.Add(recent);
        foreach (var section in _queries.Group(_queries.VisibleEmojis(catalog.Emojis), true))
            _sections.Add(section);
        _view.SetSections(_sections);
        OnSearchChanged("");
    }

    public Task WarmUpAsync(Action<double> progress) => _view.WarmUpAsync(progress);
    public void SetReady() => _view.SetReady();
    public void FocusSearch() => _view.FocusSearch();

    public void Reset()
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = null;
        _view.ResetView();
    }

    private void OnSearchChanged(string query)
    {
        bool searching = !string.IsNullOrWhiteSpace(query);
        var results = searching && _catalog is not null
            ? _queries.Search(_queries.VisibleEmojis(_catalog.Emojis), query)
            : new List<EmojiItem>();
        _view.ShowSearchResults(results, searching);
    }

    private void OnEmojiChosen(EmojiItem item)
    {
        if (_catalog is null) return;
        if (_selected is not null && _selected != item) _selected.IsSelected = false;
        _selected = item;
        item.IsSelected = true;
        // Insertion should still work if persisting the recent list fails.
        _insertion.Enqueue(item.Character);
        try
        {
            _catalog.AddRecent(item.Character);
            UpdateRecentSection();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            _view.ShowError("Could not save recent emojis.");
        }
        
    }

    private EmojiSection? BuildRecentSection()
    {
        if (_catalog is null || _catalog.RecentCharacters.Count == 0) return null;
        var items = _catalog.RecentCharacters.Where(_byCharacter.ContainsKey)
            .Select(c => new EmojiItem(_byCharacter[c])).ToList();
        return new EmojiSection("Recent", $"Showing {items.Count}/24 recent", true, items);
    }

    private void UpdateRecentSection()
    {
        var recent = BuildRecentSection();
        if (recent is null) return;
        if (_sections.Count > 0 && _sections[0].GroupName == "Recent")
        {
            var existing = _sections[0];
            existing.SubgroupName = recent.SubgroupName;
            existing.Emojis.Clear();
            foreach (var item in recent.Emojis) existing.Emojis.Add(item);
        }
        else _sections.Insert(0, recent);
    }

    public void Dispose()
    {
        _view.SearchChanged -= OnSearchChanged;
        _view.EmojiChosen -= OnEmojiChosen;
    }
}
