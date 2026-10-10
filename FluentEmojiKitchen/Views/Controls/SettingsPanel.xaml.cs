using System;
using FluentEmojiKitchen.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentEmojiKitchen.Views.Controls;

public sealed partial class SettingsPanel : UserControl
{
    public event Action<int>? ColumnsRequested;
    public event Action? RebuildRequested;
    public SettingsPanel() => InitializeComponent();
    public void SetColumns(int value) => ColumnsSlider.Value = value;
    public void FocusInput() => ColumnsSlider.Focus(FocusState.Programmatic);
    public void SetBusy(bool busy) => ColumnsSlider.IsEnabled = ApplyButton.IsEnabled = RebuildButton.IsEnabled = !busy;
    public void ShowStatus(string message) => Status.Text = message;
    private void OnApply(object sender, RoutedEventArgs args)
    {
        int value = Math.Clamp((int)Math.Round(ColumnsSlider.Value), AppOptions.MinimumColumns, AppOptions.MaximumColumns);
        ColumnsRequested?.Invoke(value);
    }
    private void OnRebuild(object sender, RoutedEventArgs args) => RebuildRequested?.Invoke();
}
