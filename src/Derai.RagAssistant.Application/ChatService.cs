using Derai.RagAssistant.Domain;
using FluentValidation;

namespace Derai.RagAssistant.Application;

public sealed record ChatRequest(string Question, string ConversationId, string Language = "es");
public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator() { RuleFor(x => x.Question).NotEmpty().MaximumLength(1000); RuleFor(x => x.ConversationId).NotEmpty().MaximumLength(80); RuleFor(x => x.Language).Must(x => x is "es" or "en"); }
}

public sealed class CorpusStores(IVectorStore sample)
{
    public IVectorStore Get(string corpus) => corpus == "nordia" ? sample : throw new ArgumentException("The only available corpus is nordia (fictional sample company).", nameof(corpus));
}

public sealed class ChatService(CorpusStores stores, IChatModel model)
{
    public async Task<ChatReply> AskAsync(ChatRequest request, string role, CancellationToken cancellationToken = default, string corpus = "nordia")
    {
        var hits = await stores.Get(corpus).SearchAsync(request.Question, role, 5, cancellationToken, request.Language);
        var answer = await model.GenerateAsync(request.Question, hits, request.Language, cancellationToken);
        var grounded = answer.StartsWith("No tengo suficiente información", StringComparison.Ordinal)
            || answer.StartsWith("I don't have enough evidence", StringComparison.Ordinal)
            || answer.StartsWith("No encuentro", StringComparison.Ordinal)
            || answer.StartsWith("I could not find enough information", StringComparison.Ordinal);
        ChatSource[] sources = grounded ? [] : hits.Select((hit, index) => new ChatSource(index + 1, hit.Chunk.File, hit.Chunk.Section,
            hit.Chunk.Text.Length > 360 ? hit.Chunk.Text[..360] + "…" : hit.Chunk.Text, Math.Round(hit.Score, 3), hit.Chunk.Language)).ToArray();
        return new(answer, sources);
    }
}
