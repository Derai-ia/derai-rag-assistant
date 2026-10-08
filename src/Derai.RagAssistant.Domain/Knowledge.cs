namespace Derai.RagAssistant.Domain;

public sealed record KnowledgeChunk(string File, string Section, string Text, string Language, string[] AllowedRoles);
public sealed record SearchHit(KnowledgeChunk Chunk, double Score);
public sealed record ChatSource(int Id, string File, string Section, string Excerpt, double Score, string Language);
public sealed record ChatReply(string Answer, IReadOnlyList<ChatSource> Sources);

public interface IVectorStore
{
    Task IndexAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string role, int take, CancellationToken cancellationToken = default, string language = "es");
}

public interface IChatModel
{
    Task<string> GenerateAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", CancellationToken cancellationToken = default);
}

public interface IEmbeddingModel
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
