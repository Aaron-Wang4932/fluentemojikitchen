using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentEmojiKitchen.Views.Controls;

public sealed partial class LoadingOverlay : UserControl
{
    public LoadingOverlay() => InitializeComponent();
    public void Show(string message, double progress)
    {
        LoadingMessage.Text = $"{message} ({progress:F0}%)";
        LoadingProgressBar.Visibility = Visibility.Visible;
        LoadingProgressBar.Value = progress;
        Visibility = Visibility.Visible;
    }
    public void ShowError(string message)
    {
        LoadingMessage.Text = message;
        LoadingProgressBar.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }
}
