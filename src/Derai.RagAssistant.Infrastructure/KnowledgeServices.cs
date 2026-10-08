using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Derai.RagAssistant.Domain;

namespace Derai.RagAssistant.Infrastructure;

public sealed class FileKnowledgeLoader(string documentsPath)
{
    public IEnumerable<KnowledgeChunk> Load()
    {
        foreach (var path in Directory.EnumerateFiles(documentsPath, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            var raw = File.ReadAllText(path);
            var roles = new[] { "direccion", "operaciones", "soporte" };
            var id = Path.GetFileNameWithoutExtension(path);
            var language = Path.GetRelativePath(documentsPath, path).Split(Path.DirectorySeparatorChar)[0];
            if (language is not ("en" or "es")) language = "es";
            if (raw.StartsWith("---", StringComparison.Ordinal))
            {
                var end = raw.IndexOf("---", 3, StringComparison.Ordinal);
                if (end > 0)
                {
                    var frontMatter = raw[3..end];
                    foreach (var line in frontMatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var separator = line.IndexOf(':');
                        if (separator < 0) continue;
                        var key = line[..separator].Trim();
                        var value = line[(separator + 1)..].Trim();
                        if (key == "id") id = value;
                        if (key == "language") language = value;
                        if (key == "allowedRoles") roles = value.Trim('[', ']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    }
                    raw = raw[(end + 3)..];
                }
            }
            var section = Path.GetFileNameWithoutExtension(path); var buffer = new StringBuilder();
            foreach (var line in raw.Split('\n'))
            {
                if (line.StartsWith('#')) { if (buffer.Length > 0) { yield return Make(path, id, language, section, buffer.ToString(), roles); buffer.Clear(); } section = line.Trim('#', ' '); }
                if (!string.IsNullOrWhiteSpace(line)) buffer.AppendLine(line);
                if (buffer.Length > 2400) { yield return Make(path, id, language, section, buffer.ToString(), roles); buffer.Clear(); }
            }
            if (buffer.Length > 0) yield return Make(path, id, language, section, buffer.ToString(), roles);
        }
    }
    private static KnowledgeChunk Make(string path, string id, string language, string section, string text, string[] roles) => new($"{id}{Path.GetExtension(path)}", section, text.Trim(), language, roles);
}

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<KnowledgeChunk> _chunks = [];
    private readonly Dictionary<KnowledgeChunk, float[]> _vectors = [];
    private readonly IEmbeddingModel _embeddings;
    public InMemoryVectorStore(IEmbeddingModel? embeddings = null) => _embeddings = embeddings ?? new HashEmbeddingModel();
    public async Task IndexAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        _chunks.Clear(); _vectors.Clear();
        foreach (var chunk in chunks) { cancellationToken.ThrowIfCancellationRequested(); _chunks.Add(chunk); _vectors[chunk] = await _embeddings.EmbedAsync(chunk.Text, cancellationToken); }
    }
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string role, int take, CancellationToken cancellationToken = default, string language = "es")
    {
        var q = Terms(query); var permitted = _chunks.Where(x => x.AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToArray(); var queryVector = await _embeddings.EmbedAsync(query, cancellationToken);
        var vectorRank = permitted.Select(c => (chunk: c, score: Cosine(queryVector, _vectors[c]))).Where(x => x.score > 0).OrderByDescending(x => x.score).ToArray();
        var keywordRank = permitted.Select(c => { var terms = Terms(c.Text); var score = q.Sum(t => terms.Contains(t) ? 1d / (1 + terms.Count(x => x == t)) : 0); return (chunk: c, score); }).Where(x => x.score > 0).OrderByDescending(x => x.score).ToArray();
        var ranks = new Dictionary<KnowledgeChunk, double>();
        for (var i = 0; i < vectorRank.Length; i++) ranks[vectorRank[i].chunk] = ranks.GetValueOrDefault(vectorRank[i].chunk) + 1d / (60 + i + 1);
        for (var i = 0; i < keywordRank.Length; i++) ranks[keywordRank[i].chunk] = ranks.GetValueOrDefault(keywordRank[i].chunk) + 1d / (60 + i + 1);
        var ranked = ranks.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Select(x => new SearchHit(x.Key, x.Value)).ToArray();
        var preferred = ranked.Where(x => x.Chunk.Language.Equals(language, StringComparison.OrdinalIgnoreCase) && x.Score >= .02).Take(take).ToArray();
        return preferred.Length > 0 ? preferred : ranked.Where(x => !x.Chunk.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).Take(take).ToArray();
    }
    private static string[] Terms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}").Select(m => m.Value).ToArray();
    private static double Cosine(float[] a, float[] b) { var dot = a.Zip(b, (x, y) => x * y).Sum(); var norm = Math.Sqrt(a.Sum(x => x * x) * b.Sum(x => x * x)); return norm == 0 ? 0 : dot / norm; }
}

public sealed class FakeChatModel : IChatModel
{
    public Task<string> GenerateAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", CancellationToken cancellationToken = default)
    {
        var english = language == "en";
        if (context.Count == 0 || context[0].Score < .02) return Task.FromResult(english ? "I could not find enough information in the documents available to your role. Please contact a team member to confirm." : "No encuentro información suficiente en los documentos autorizados. Contacta con una persona del equipo para confirmarlo.");
        var best = context[0].Chunk.Text.Replace("\n", " ").Trim();
        if (best.Contains("ignora las instrucciones", StringComparison.OrdinalIgnoreCase) || best.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(english ? "I could not find enough information in the documents available to your role. Please contact a team member to confirm." : "No encuentro información suficiente en los documentos autorizados. Contacta con una persona del equipo para confirmarlo.");
        return Task.FromResult(english ? $"According to the authorized documentation: {best} [1]" : $"Según la documentación disponible: {best} [1]");
    }

    public async IAsyncEnumerable<string> StreamAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var answer = await GenerateAsync(question, context, language, cancellationToken);
        foreach (var token in answer.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { cancellationToken.ThrowIfCancellationRequested(); yield return token + " "; }
    }
}

