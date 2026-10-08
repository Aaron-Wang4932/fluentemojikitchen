namespace FluentEmojiKitchen.Contracts;

public interface IEmojiInsertionQueue
{
    void Enqueue(string character);
}
