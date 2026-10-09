using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Configuration;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Presenters;
using FluentEmojiKitchen.Services;

static class Program
{
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    static EmojiModel Emoji(string key) => new(char.ConvertFromUtf32(Convert.ToInt32(key, 16)), new[] { Convert.ToInt32(key, 16) }, key, "test", "test");
    static readonly string U1 = "https://www.gstatic.com/android/keyboard/emojikitchen/20201001/u2615/u2615_u2615.png";
    static readonly string U2 = "https://www.gstatic.com/android/keyboard/emojikitchen/20201001/u2615/u2615_u1f600.png";
    static readonly string U3 = "https://www.gstatic.com/android/keyboard/emojikitchen/20230301/u2615/u2615_u1f600.png";
    static async Task Main(string[] args)
    {
        string dir = Path.Combine(Path.GetTempPath(), "KitchenTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Large irrelevant token crosses buffers; reversed records and multiple latest versions are intentional.
            string raw = Path.Combine(dir, "kitchen-metadata.json");
            string fixture = JsonSerializer.Serialize(new {
                knownSupportedEmoji = new[] { "2615", "1f600", "1f62d" },
                data = new Dictionary<string, object> {
                    ["2615"] = new { keywords = new[] { new string('x', 70000) }, combinations = new Dictionary<string, object> {
                        ["2615"] = new[] { new { isLatest = true, gStaticUrl = U1 } },
                        ["1f600"] = new[] { new { isLatest = false, gStaticUrl = U1 }, new { isLatest = true, gStaticUrl = U2 }, new { isLatest = true, gStaticUrl = U3 } } } },
                    ["1f600"] = new { combinations = new Dictionary<string, object> { ["2615"] = new[] { new { gStaticUrl = U2, isLatest = true } } } }
                }
            });
            File.WriteAllText(raw, fixture);
            var parser = new KitchenMetadataParser();
            var index = parser.Parse(raw);
            Check(index.Urls.Count == 3 && index.Latest["1f600|2615"].Length == 2, "latest URL deduplication");
            var compatibility = new KitchenCompatibilityService(index);
            var coffee = Emoji("2615"); var smile = Emoji("1f600"); var cry = Emoji("1f62d");
            Check(compatibility.CanCombine(coffee, coffee), "self pair");
            Check(compatibility.CanCombine(smile, coffee) && compatibility.CanCombine(coffee, smile), "symmetric pair");
            Check(!compatibility.CanCombine(coffee, cry), "unsupported pair");
            Check(compatibility.Supports(cry), "supported endpoint with empty compatibility");
            File.WriteAllText(Path.Combine(dir, "truncated.json"), fixture[..^4]);
            try { parser.Parse(Path.Combine(dir, "truncated.json")); throw new Exception("accepted truncation"); }
            catch (JsonException) { }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { parser.Parse(raw, cancellationToken: cancelled.Token); throw new Exception("ignored cancellation"); }
            catch (OperationCanceledException) { }
            Console.WriteLine("PASS parser: buffer boundaries, all latest variants, deduplication, self/reverse pairs, truncation, cancellation");

            var cache = new KitchenMetadataCache(dir, parser);
            await cache.LoadAsync(null, CancellationToken.None);
            string compact = Path.Combine(dir, "kitchen-index.json.gz");
            Check(File.Exists(compact), "compact cache created");
            File.Delete(raw);
            Check((await cache.LoadAsync(null, CancellationToken.None)).Urls.Count == 3, "compact used without raw/network");
            File.WriteAllText(raw, fixture);
            File.WriteAllText(compact, "broken gzip");
            Check((await cache.LoadAsync(null, CancellationToken.None)).Urls.Count == 3, "corrupt compact rebuilt");
            Console.WriteLine("PASS cache: compressed round trip, compact-only startup, damaged compact recovery");

            ImageRetentionTests(dir);
            await PresenterTests(compatibility, coffee, smile, cry);
            await StartupTests(compatibility, new[] { coffee, smile, cry });
            if (args.Length > 0) await FullMetadataTest(args[0], dir);
        }
        finally { Directory.Delete(dir, true); }
    }
    static void ImageRetentionTests(string root)
    {
        string dir = Path.Combine(root, "images"); Directory.CreateDirectory(dir);
        using var cache = new KitchenImageCache(dir);
        string old = Path.Combine(dir, new string('A', 64) + ".png");
        string recent = Path.Combine(dir, new string('B', 64) + ".png");
        string pinned = Path.Combine(dir, new string('C', 64) + ".png");
        string temp = old + "." + new string('d', 32) + ".tmp";
        string other = Path.Combine(dir, "unrelated.png");
        foreach (var path in new[] { old, recent, pinned, temp, other }) File.WriteAllText(path, "fixture");
        using (cache.Acquire(pinned))
        {
            foreach (var path in new[] { old, pinned, temp, other }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
            cache.Sweep(DateTime.UtcNow);
            Check(!File.Exists(old) && !File.Exists(temp), "expired owned images and abandoned temporary files removed");
            Check(File.Exists(recent) && File.Exists(pinned) && File.Exists(other), "recent, leased and unrelated files preserved");
        }
        Check(File.GetLastWriteTimeUtc(pinned) > DateTime.UtcNow.AddMinutes(-1), "last-use time refreshed after release");
        File.SetLastWriteTimeUtc(pinned, DateTime.UtcNow.AddDays(-8));
        cache.Sweep(DateTime.UtcNow);
        Check(!File.Exists(pinned), "released expired file can be evicted");
        Console.WriteLine("PASS image retention: seven-day expiry, active leases, last-use refresh, owned-file-only cleanup");
    }
    static async Task PresenterTests(IKitchenCompatibility compatibility, params EmojiModel[] models)
    {
        var view = new FakeKitchenView(); var service = new FakeKitchenService();
        using var presenter = new KitchenPresenter(view, new EmojiQueryService(), service);
        presenter.Initialize(new FakeCatalog(models), compatibility);
        var coffee = view.Items.Single(i => i.Model == models[0]);
        var smile = view.Items.Single(i => i.Model == models[1]);
        var cry = view.Items.Single(i => i.Model == models[2]);
        view.Open(KitchenSlot.Left);
        Check(view.Items.All(i => i.IsEnabled), "first slot available");
        view.Choose(coffee);
        view.Open(KitchenSlot.Right);
        Check(coffee.IsEnabled && smile.IsEnabled && !cry.IsEnabled, "right slot filters");
        view.Search("1f62d"); Check(view.Results.Count == 1 && !view.Results[0].IsEnabled, "disabled search result");
        view.Choose(cry); Check(view.State!.Right is null, "presenter rejects disabled input");
        view.Choose(smile); var old = service.Requests.Last();
        view.Open(KitchenSlot.Left);
        Check(coffee.IsEnabled && !smile.IsEnabled && !cry.IsEnabled, "left slot filters against right");
        int renders = view.Renders;
        old.SetResult(new(new Uri("file:///old.png"), null));
        await Task.Delay(30);
        Check(view.Renders == renders && view.State!.ActiveSlot == KitchenSlot.Left, "stale completion ignored");
        view.Clear(KitchenSlot.Right);
        Check(view.Items.All(i => i.IsEnabled), "clearing opposite re-enables tiles");
        view.Choose(coffee); view.Open(KitchenSlot.Right); view.Choose(coffee);
        int requests = service.Requests.Count;
        view.Resize(512);
        Check(service.Requests.Count == requests, "size change during loading does not restart download");
        service.Requests.Last().SetResult(new(new Uri("file:///new.png"), null, CanCopy: true));
        await Task.Delay(30);
        Check(view.State!.Result?.ImageUri?.LocalPath == "/new.png", "current image result rendered");
        Check(view.State!.Result!.CopySize == 512, "size selected during loading preserved");
        var source = view.State.Result.ImageUri;
        view.Resize(48);
        Check(service.Requests.Count == requests && view.State.Result.ImageUri == source, "size changes preserve preview source and request count");
        view.Copy();
        Check(service.LastCopySize == 48, "copy receives selected export size");
        Console.WriteLine("PASS fixed preview: size changes during/after download do not reload; copy uses selected size");
        Console.WriteLine("PASS presenter: both slot directions, search, invalid selection, clear, self pair, stale/current results");
    }
    static async Task StartupTests(IKitchenCompatibility compatibility, EmojiModel[] models)
    {
        foreach (bool warmup in new[] { false, true })
        {
            var view = new FakeMainView(); var popup = new FakePopup(); var layout = new FakeLayout();
            var loader = new FakeLoader(); var emojiView = new FakeEmojiView();
            var emoji = new EmojiPanelPresenter(emojiView, new EmojiQueryService(), new FakeInsertion());
            var kitchen = new KitchenPresenter(new FakeKitchenView(), new EmojiQueryService(), new FakeKitchenService());
            using var main = new MainPresenter(view, popup, layout, emoji, kitchen, loader,
                new AppOptions { PrecacheEmojis = warmup });
            var task = main.InitializeAsync();
            Check(view.Loading && popup.Toggles == 0 && !emojiView.Ready, "startup remains hidden with overlay prepared");
            view.SelectTab(); Check(view.TabChanges == 0, "tabs gated during loading");
            loader.Completion.SetResult(new(new FakeCatalog(models), compatibility));
            await emojiView.SectionsSet.Task;
            Check(!layout.Entered.Task.IsCompleted && emojiView.WarmupCalls == 0, "hidden startup does not realize layout");
            main.TogglePopup();
            await layout.Entered.Task;
            Check(view.Loading && !emojiView.Ready, "waits for layout");
            layout.Completion.SetResult();
            if (warmup)
            {
                await emojiView.Entered.Task;
                Check(view.Loading && !emojiView.Ready, "waits for optional warmup");
                emojiView.Completion.SetResult();
            }
            await task;
            Check(!view.Loading && view.Percent == 100 && emojiView.Ready, "ready after all phases");
            Check(emojiView.WarmupCalls == (warmup ? 1 : 0), "warmup option respected");
            view.SelectTab(); Check(view.TabChanges == 1, "tabs enabled after loading");
        }
        Console.WriteLine("PASS startup: hidden launch, explicit opening, interaction gate, layout wait, warmup on/off, final readiness");
    }
    static async Task FullMetadataTest(string path, string dir)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var index = new KitchenMetadataParser().Parse(path);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var expected = new HashSet<string>();
        var allPairs = new HashSet<string>();
        var latestByPair = new Dictionary<string, HashSet<string>>();
        foreach (var emoji in doc.RootElement.GetProperty("data").EnumerateObject())
        foreach (var partner in emoji.Value.GetProperty("combinations").EnumerateObject())
        foreach (var version in partner.Value.EnumerateArray())
        {
            string pair = KitchenMetadataParser.PairKey(emoji.Name, partner.Name);
            allPairs.Add(pair);
            if (!version.GetProperty("isLatest").GetBoolean()) continue;
            string url = version.GetProperty("gStaticUrl").GetString()!;
            expected.Add(url);
            if (!latestByPair.TryGetValue(pair, out var set)) latestByPair[pair] = set = new();
            set.Add(url);
        }
        Check(expected.SetEquals(index.Urls), "all latest URLs preserved");
        foreach (var pair in allPairs)
        {
            var keys = pair.Split('|');
            Check(index.Compatible[keys[0]].Contains(keys[1]) && index.Compatible[keys[1]].Contains(keys[0]), "every compatibility preserved");
        }
        foreach (var pair in latestByPair)
            Check(pair.Value.SetEquals(index.Latest[pair.Key].Select(id => index.Urls[id])), "all per-pair URLs preserved");
        string json = JsonSerializer.Serialize(index);
        string compact = Path.Combine(dir, "full-index.json.gz");
        using (var file = File.Create(compact)) using (var gzip = new GZipStream(file, CompressionLevel.Optimal)) JsonSerializer.Serialize(gzip, index);
        Console.WriteLine($"PASS full metadata: {index.Supported.Count} supported, {allPairs.Count} pairs, {index.Urls.Count} unique latest URLs, {json.Length:N0} JSON chars, {new FileInfo(compact).Length:N0} gzip bytes, {watch.Elapsed.TotalSeconds:F2}s including independent verification");
        using var images = new OnlineKitchenService(Path.Combine(dir, "images"));
        images.Initialize(new KitchenCompatibilityService(index));
        var coffee = Emoji("2615");
        var result = await images.CreateAsync(coffee, coffee, 48, CancellationToken.None);
        Check(result.ImageUri is not null && File.Exists(result.ImageUri.LocalPath), "real image downloaded");
        var original = File.ReadAllBytes(result.ImageUri!.LocalPath);
        await images.CreateAsync(coffee, coffee, 128, CancellationToken.None);
        Check(File.ReadAllBytes(result.ImageUri.LocalPath).SequenceEqual(original), "cached original preserved across export-size changes");
        Console.WriteLine("PASS real image download and cache reuse");
    }
}
sealed class FakeCatalog : IEmojiCatalog
{
    public FakeCatalog(IReadOnlyList<EmojiModel> models) { Emojis = SupportedFusionEmojis = models; }
    public IReadOnlyList<EmojiModel> Emojis { get; }
    public IReadOnlyList<EmojiModel> SupportedFusionEmojis { get; }
    public IReadOnlyList<string> RecentCharacters => Array.Empty<string>();
    public void AddRecent(string character) { }
}
sealed class FakeKitchenService : IKitchenService
{
    public readonly List<TaskCompletionSource<KitchenResult>> Requests = new();
    public Task<KitchenResult> CreateAsync(EmojiModel l, EmojiModel r, int size, CancellationToken token)
    { var t = new TaskCompletionSource<KitchenResult>(TaskCreationOptions.RunContinuationsAsynchronously); Requests.Add(t); return t.Task; }
    public int LastCopySize;
    public Task CopyAsync(KitchenResult r, CancellationToken t) { LastCopySize = r.CopySize; return Task.CompletedTask; }
}
sealed class FakeKitchenView : IKitchenView
{
    public event Action<KitchenSlot>? SlotTapped;
    public event Action<KitchenSlot>? SlotCleared;
    public event Action<EmojiItem>? EmojiChosen;
    public event Action<string>? SearchChanged;
    public event Action<int>? SizeChangedRequested;
    public event Action? CopyRequested;
    public List<EmojiItem> Items = new();
    public IReadOnlyList<EmojiItem> Results = Array.Empty<EmojiItem>();
    public KitchenViewState? State; public int Renders;
    public void SetSections(IEnumerable<EmojiSection> sections) => Items = sections.SelectMany(s => s.Emojis).ToList();
    public void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching) => Results = results;
    public void ResetSearch() => SearchChanged?.Invoke("");
    public void Render(KitchenViewState state) { State = state; Renders++; }
    public void Open(KitchenSlot slot) => SlotTapped?.Invoke(slot);
    public void Clear(KitchenSlot slot) => SlotCleared?.Invoke(slot);
    public void Choose(EmojiItem item) => EmojiChosen?.Invoke(item);
    public void Search(string query) => SearchChanged?.Invoke(query);
    public void Resize(int size) => SizeChangedRequested?.Invoke(size);
    public void Copy() => CopyRequested?.Invoke();
}

sealed class FakeMainView : IMainView
{
    public event Action<PanelTab>? TabRequested;
    public event Action? CloseRequested;
    public bool Loading; public double Percent; public int TabChanges;
    public void ShowTab(PanelTab tab) => TabChanges++;
    public void SetLoading(bool loading, string message, double progress) { Loading = loading; Percent = progress; }
    public void ShowInitializationError(string message) => Loading = true;
    public void SelectTab() => TabRequested?.Invoke(PanelTab.Kitchen);
}
sealed class FakePopup : IPopupWindow
{
    public event Action? Hidden; public event Action? Shown; public int Toggles;
    public void Toggle() { Toggles++; Shown?.Invoke(); }
    public void Hide() => Hidden?.Invoke();
}
sealed class FakeLayout : IWindowLayout
{
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task FitColumnsAsync() { Entered.SetResult(); return Completion.Task; }
    public void FinishLoading() { }
}
sealed class FakeLoader : IStartupDataLoader
{
    public TaskCompletionSource<StartupData> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<StartupData> LoadAsync(IProgress<StartupProgress> progress, CancellationToken cancellationToken) => Completion.Task;
}
sealed class FakeInsertion : IEmojiInsertionQueue { public void Enqueue(string character) { } }
sealed class FakeEmojiView : IEmojiPanelView
{
    public event Action<string>? SearchChanged;
    public event Action<EmojiItem>? EmojiChosen;
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Ready; public int WarmupCalls;
    public TaskCompletionSource SectionsSet = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void SetSections(IEnumerable<EmojiSection> sections) { SectionsSet.TrySetResult(); }
    public void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching) { }
    public void ResetView() { }
    public void FocusSearch() { }
    public void SetReady() => Ready = true;
    public void ShowError(string message) { }
    public Task WarmUpAsync(Action<double> progress) { WarmupCalls++; Entered.SetResult(); return Completion.Task; }
}
