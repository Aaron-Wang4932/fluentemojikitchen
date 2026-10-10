namespace FluentEmojiKitchen.Configuration;

public sealed class AppOptions
{
    // Matches the current uploaded project. Enable to run the scroll-through warmup.
    public bool PrecacheEmojis { get; init; } = false;
    public const int MinimumColumns = 6, MaximumColumns = 20;
    public int TargetColumns { get; set; } = 6;
    public int WindowHeight { get; init; } = 600;
}
