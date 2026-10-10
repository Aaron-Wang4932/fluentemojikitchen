using System;
using System.Collections.Generic;
using FluentEmojiKitchen.Contracts;
using FluentEmojiKitchen.Models;
using FluentEmojiKitchen.Presentation;
using FluentEmojiKitchen.Views.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
namespace FluentEmojiKitchen.Views;

public sealed partial class KitchenPanelView : UserControl, IKitchenView
{
    public event Action<KitchenSlot>? SlotTapped;
    public event Action<KitchenSlot>? SlotCleared;
    public event Action<EmojiItem>? EmojiChosen;
    public event Action<string>? SearchChanged;
    public event Action<int>? SizeChangedRequested;
    public event Action? CopyRequested;
    public event Action? CopyURLRequested;
    private Uri? _displayedImage;
    private BitmapImage? _bitmap;
    private bool _imageLoading;
    public event Action? SearchFocusRequested;
    public KitchenPanelView()
    {
        InitializeComponent();
        KitchenSearchBox.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => SearchFocusRequested?.Invoke()), true);
    }
    public void FocusSearch() => KitchenSearchBox.Focus(FocusState.Programmatic);
    public void SetSections(IEnumerable<EmojiSection> sections) => KitchenPickerRepeater.ItemsSource = sections;
    public void ShowSearchResults(IReadOnlyList<EmojiItem> results, bool searching)
    {
        KitchenPickerSearchResultsRepeater.ItemsSource = results;
        KitchenPickerGroupedContent.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        KitchenPickerSearchResultsContent.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
    }
    public void ResetSearch() => KitchenSearchBox.Text = "";
    public void Render(KitchenViewState state)
    {
        LeftSlot.Render(state.Left, state.ActiveSlot == KitchenSlot.Left);
        RightSlot.Render(state.Right, state.ActiveSlot == KitchenSlot.Right);
        bool picking = state.ActiveSlot != KitchenSlot.None;
        bool result = !picking && state.Left is not null && state.Right is not null;
        KitchenPickerContent.Visibility = picking ? Visibility.Visible : Visibility.Collapsed;
        KitchenResultContent.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
        KitchenHintText.Visibility = !picking && !result ? Visibility.Visible : Visibility.Collapsed;
        KitchenResultLoadingRing.IsActive = state.IsLoading || _imageLoading;
        KitchenCopyButton.IsEnabled = !state.IsLoading && !state.IsCopying && state.Result is { CanCopy: true, ImageUri: not null };
        KitchenURLCopyButton.IsEnabled = !state.IsLoading && !state.IsCopying && state.Result is { CanCopy: true, ImageUri: not null };
        ResultMessage.Text = state.Result?.Message ?? "";
        // Preview dimensions belong exclusively to KitchenImageFrame in XAML.
        // Decode the original image; export size must not reduce preview quality.
        var uri = state.Result?.ImageUri;
        if (uri != _displayedImage)
        {
            _displayedImage = uri;
            _bitmap = null;
            _imageLoading = false;
            KitchenResultLoadingRing.IsActive = state.IsLoading;
            KitchenResultImage.Source = null;
            if (uri is not null)
            {
                var bitmap = new BitmapImage();
                _bitmap = bitmap;
                _imageLoading = true;
                KitchenResultLoadingRing.IsActive = true;
                bitmap.ImageOpened += (_, _) =>
                {
                    if (ReferenceEquals(_bitmap, bitmap))
                    { _imageLoading = false; KitchenResultLoadingRing.IsActive = false; }
                };
                bitmap.ImageFailed += (_, _) =>
                {
                    if (!ReferenceEquals(_bitmap, bitmap)) return;
                    _imageLoading = false;
                    KitchenResultLoadingRing.IsActive = false;
                    ResultMessage.Text = "Could not display this image. Select the pair again to retry.";
                };
                KitchenResultImage.Source = bitmap;
                bitmap.UriSource = uri;
            }
        }
    }
    private void OnSlotTapped(object? sender, EventArgs e) => SlotTapped?.Invoke(GetSlot(sender));
    private void OnSlotCleared(object? sender, EventArgs e) => SlotCleared?.Invoke(GetSlot(sender));
    private static KitchenSlot GetSlot(object? sender) => sender is FrameworkElement { Tag: "Left" } ? KitchenSlot.Left : KitchenSlot.Right;
    private void OnEmojiChosen(object? sender, EmojiChosenEventArgs e) => EmojiChosen?.Invoke(e.Item);
    private void OnKitchenSearchTextChanged(object sender, TextChangedEventArgs e) => SearchChanged?.Invoke(KitchenSearchBox.Text);
    private void OnKitchenSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } } && int.TryParse(tag, out int size))
            SizeChangedRequested?.Invoke(size);
    }
    private void OnKitchenCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();

    private void OnKitchenCopyURLClick(object sender, RoutedEventArgs e)
    {
        CopyURLRequested?.Invoke();
    }
}
