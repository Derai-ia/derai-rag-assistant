namespace Derai.RagAssistant.Api.Services;

public sealed class ConversationHistory
{
    private readonly Dictionary<string, Queue<string>> _items = new();

    public void Add(string id, string question)
    {
        if (!_items.TryGetValue(id, out var queue))
            _items[id] = queue = new Queue<string>();
        queue.Enqueue(question);
        while (queue.Count > 8)
            queue.Dequeue();
    }
}
