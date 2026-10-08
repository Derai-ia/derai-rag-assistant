using Derai.RagAssistant.Domain;
using FluentValidation;

namespace Derai.RagAssistant.Application;

public sealed record ChatRequest(string Question, string ConversationId, string Language = "es");
public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator() { RuleFor(x => x.Question).NotEmpty().MaximumLength(1000); RuleFor(x => x.ConversationId).NotEmpty().MaximumLength(80); RuleFor(x => x.Language).Must(x => x is "es" or "en"); }
}

public sealed class ChatService(IVectorStore store, IChatModel model)
{
    public async Task<ChatReply> AskAsync(ChatRequest request, string role, CancellationToken cancellationToken = default)
    {
        var hits = await store.SearchAsync(request.Question, role, 5, cancellationToken, request.Language);
        var answer = await model.GenerateAsync(request.Question, hits, request.Language, cancellationToken);
        var grounded = answer.StartsWith("No encuentro", StringComparison.Ordinal) || answer.StartsWith("I could not find enough information", StringComparison.Ordinal);
        ChatSource[] sources = grounded ? [] : hits.Select((hit, index) => new ChatSource(index + 1, hit.Chunk.File, hit.Chunk.Section,
            hit.Chunk.Text.Length > 360 ? hit.Chunk.Text[..360] + "…" : hit.Chunk.Text, Math.Round(hit.Score, 3), hit.Chunk.Language)).ToArray();
        return new(answer, sources);
    }
}
