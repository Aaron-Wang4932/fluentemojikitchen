using System.Threading.Tasks;

namespace FluentEmojiKitchen.Contracts;

public interface IWindowLayout
{
    Task FitColumnsAsync();
    void FinishLoading();
    void ResetFit();
}
