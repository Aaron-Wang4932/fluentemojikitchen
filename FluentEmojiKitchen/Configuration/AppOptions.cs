namespace FluentEmojiKitchen.Configuration;

public sealed class AppOptions
{
    // Matches the current uploaded project. Enable to run the scroll-through warmup.
    public bool PrecacheEmojis { get; init; } = false;
    public int TargetColumns { get; init; } = 6;
    public int WindowHeight { get; init; } = 480;
}
