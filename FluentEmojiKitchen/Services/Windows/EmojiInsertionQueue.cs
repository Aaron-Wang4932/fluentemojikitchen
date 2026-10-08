using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentEmojiKitchen.Contracts;
namespace FluentEmojiKitchen.Services.Windows;

public sealed class EmojiInsertionQueue : IEmojiInsertionQueue, IDisposable
{
    private readonly Queue<(int Session, string Character)> _pending = new();
    private readonly PopupWindowService _popup;
    private bool _processing, _disposed;
    public EmojiInsertionQueue(PopupWindowService popup) => _popup = popup;
    public void Enqueue(string character)
    {
        if (_disposed || _popup.CurrentSession == 0) return;
        _pending.Enqueue((_popup.CurrentSession, character));
        if (!_processing) _ = ProcessAsync();
    }
    private async Task ProcessAsync()
    {
        _processing = true;
        try
        {
            while (!_disposed && _pending.Count > 0)
            {
                var item = _pending.Dequeue();
                if (!await _popup.PrepareInsertionAsync(item.Session) || _disposed || !_popup.CanInsert(item.Session))
                {
                    System.Diagnostics.Debug.WriteLine("Emoji insertion cancelled: popup dismissed or target changed.");
                    continue;
                }
                EmojiInsertionService.InsertText(item.Character);
                await Task.Delay(40);
            }
        }
        catch (Exception ex)
        {
            _pending.Clear();
            System.Diagnostics.Debug.WriteLine($"Emoji insertion failed: {ex}");
        }
        finally { _processing = false; }
    }
    public void Dispose() { _disposed = true; _pending.Clear(); }
}
