using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Derai.RagAssistant.Domain;

namespace Derai.RagAssistant.Infrastructure;

public sealed class FileKnowledgeLoader(string documentsPath)
{
    public IEnumerable<KnowledgeChunk> Load(string corpus = "derai")
    {
        var corpusPath = Path.Combine(documentsPath, corpus);
        foreach (var path in Directory.EnumerateFiles(corpusPath, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            var raw = File.ReadAllText(path);
            var roles = new[] { "direccion", "operaciones", "soporte" };
            var id = Path.GetFileNameWithoutExtension(path);
            var language = Path.GetRelativePath(corpusPath, path).Split(Path.DirectorySeparatorChar)[0];
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
                if (line.StartsWith('#'))
                {
                    if (buffer.Length > 0) { yield return Make(path, id, language, section, buffer.ToString(), roles); buffer.Clear(); }
                    section = line.Trim('#', ' ');
                    continue;
                }
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
        foreach (var chunk in chunks) { cancellationToken.ThrowIfCancellationRequested(); _chunks.Add(chunk); _vectors[chunk] = await _embeddings.EmbedAsync(chunk.Text, "passage", cancellationToken); }
    }
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string role, int take, CancellationToken cancellationToken = default, string language = "es")
    {
        var q = Terms(query); var permitted = _chunks.Where(x => x.AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToArray(); var queryVector = await _embeddings.EmbedAsync(query, "query", cancellationToken);
        var vectorRank = permitted.Select(c => (chunk: c, score: Cosine(queryVector, _vectors[c]))).Where(x => x.score > 0).OrderByDescending(x => x.score).ToArray();
        var keywordRank = permitted.Select(c => { var terms = Terms($"{c.Text} {c.Section} {c.File}"); var score = q.Sum(t => terms.Contains(t) ? 1d / (1 + terms.Count(x => x == t)) : 0); return (chunk: c, score); }).Where(x => x.score > 0).OrderByDescending(x => x.score).ToArray();
        var ranks = new Dictionary<KnowledgeChunk, double>();
        for (var i = 0; i < vectorRank.Length; i++) ranks[vectorRank[i].chunk] = ranks.GetValueOrDefault(vectorRank[i].chunk) + 1d / (60 + i + 1);
        for (var i = 0; i < keywordRank.Length; i++) ranks[keywordRank[i].chunk] = ranks.GetValueOrDefault(keywordRank[i].chunk) + 1d / (60 + i + 1);
        var ranked = ranks.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Select(x => new SearchHit(x.Key, x.Value)).ToArray();
        var preferred = ranked.Where(x => x.Chunk.Language.Equals(language, StringComparison.OrdinalIgnoreCase) && x.Score >= .015).Take(take).ToArray();
        return preferred.Length > 0 ? preferred : ranked.Where(x => !x.Chunk.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).Take(take).ToArray();
    }
    private static string[] Terms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
        .Select(match => match.Value.Normalize(NormalizationForm.FormD))
        .Select(term => new string(term.Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()))
        .Select(term => term.Length > 5 && term.EndsWith("es", StringComparison.Ordinal) ? term[..^2] : term.Length > 4 && term.EndsWith('s') ? term[..^1] : term)
        .ToArray();
    private static double Cosine(float[] a, float[] b) { var dot = a.Zip(b, (x, y) => x * y).Sum(); var norm = Math.Sqrt(a.Sum(x => x * x) * b.Sum(x => x * x)); return norm == 0 ? 0 : dot / norm; }
}

public sealed class FakeChatModel : IChatModel
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "que", "como", "con", "para", "por", "una", "uno", "las", "los", "del", "the", "what", "how", "does", "can", "you", "are", "is", "and", "for", "with", "from", "this", "that", "about", "please", "tell", "me", "do", "we", "it", "euro", "euros", "eur", "file", "request", "march", "marzo"
    };

    public Task<string> GenerateAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", CancellationToken cancellationToken = default)
    {
        var english = language == "en";
        if (context.Count == 0 || context[0].Score < .015)
            return Task.FromResult(Abstention(english));

        var questionTerms = Terms(question).Where(term => !StopWords.Contains(term)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Regex.IsMatch(question, @"\b(deadline|due|timeframe|plazo|cuánto tarda|how long)\b", RegexOptions.IgnoreCase))
            questionTerms.UnionWith(Terms(language == "en" ? "days business within" : "días laborables dentro"));
        if (Regex.IsMatch(question, @"\b(how.*work|cómo.*funciona|como.*funciona)\b", RegexOptions.IgnoreCase))
            questionTerms.UnionWith(Terms("process discover design build evolve"));
        if (Regex.IsMatch(question, @"\b(how much|sales|revenue|sell|cuanto|ventas|ingresos)\b", RegexOptions.IgnoreCase))
            questionTerms.UnionWith(Terms("sales revenue ventas ingresos"));
        if (questionTerms.Count == 0) return Task.FromResult(Abstention(english));

        var ranked = context.Select((hit, index) => new
        {
            Hit = hit,
            Source = index + 1,
            Sentences = CleanText(hit.Chunk.Text).Select((sentence, sentenceIndex) => new
            {
                Text = sentence,
                SentenceIndex = sentenceIndex,
                Overlap = Terms(sentence).Where(term => !StopWords.Contains(term)).Distinct(StringComparer.OrdinalIgnoreCase).Count(questionTerms.Contains),
                MetadataOverlap = Terms($"{hit.Chunk.Section} {hit.Chunk.File}").Distinct(StringComparer.OrdinalIgnoreCase).Count(questionTerms.Contains)
            }).Where(item => item.Overlap > 0 || (item.MetadataOverlap >= (questionTerms.Count == 1 ? 1 : 2) && item.SentenceIndex == 0)).OrderByDescending(item => item.Overlap).ToArray()
        })
            .Where(item => item.Hit.Score >= .015 && !IsPromptInjection(item.Hit.Chunk.Text) && item.Sentences.Length > 0)
            .SelectMany(item => item.Sentences.Take(3).Select(sentence => (sentence.Text, item.Source, Relevance: sentence.Overlap + 2 * sentence.MetadataOverlap, item.Hit.Score)))
            .OrderByDescending(item => item.Relevance)
            .ThenByDescending(item => item.Score)
            .ToArray();

        if (ranked.Length == 0) return Task.FromResult(Abstention(english));

        var isProcedure = IsProcedureQuestion(question, english);
        var maxWords = isProcedure ? 180 : 120;
        var selected = new List<string>();
        var words = 0;
        foreach (var item in ranked)
        {
            if (selected.Count >= (isProcedure ? 6 : 4)) break;
            if (selected.Any(existing => string.Equals(existing, item.Text, StringComparison.OrdinalIgnoreCase))) continue;
            var sentenceWords = Regex.Matches(item.Text, @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?").Count;
            if (words > 0 && words + sentenceWords > maxWords) continue;
            selected.Add($"{item.Text} [{item.Source}]");
            words += sentenceWords;
            if (!isProcedure && selected.Count == 2) break;
        }

        if (selected.Count == 0) return Task.FromResult(Abstention(english));
        return Task.FromResult(isProcedure && selected.Count > 1
            ? string.Join("\n", selected.Select(item => $"- {item}"))
            : string.Join(" ", selected));
    }

    private static string Abstention(bool english) => english
        ? "I don't have enough evidence to answer confidently. Would you like me to connect you with someone from the team?"
        : "No tengo suficiente información para responder con seguridad. ¿Quieres que te ponga en contacto con alguien del equipo?";

    private static bool IsPromptInjection(string text) => text.Contains("ignora las instrucciones", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase);

    private static bool IsProcedureQuestion(string question, bool english) => english
        ? Regex.IsMatch(question, @"\b(how|steps|procedure|process)\b", RegexOptions.IgnoreCase)
        : Regex.IsMatch(question, @"\b(cómo|como|pasos|procedimiento|proceso)\b", RegexOptions.IgnoreCase);

    private static IEnumerable<string> CleanText(string text)
    {
        var plain = Regex.Replace(text, @"\A\s*---\s*\r?\n.*?\r?\n---\s*\r?\n", "", RegexOptions.Singleline);
        foreach (var rawLine in plain.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || Regex.IsMatch(line, @"^#{1,6}\s*")) continue;
            line = Regex.Replace(line, @"^\s*(?:[-*+]\s+|\d+[.)]\s+)", "");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = Regex.Replace(line, @"!?\[([^\]]*)\]\[[^\]]*\]", "$1");
            line = Regex.Replace(line, @"https?://\S+", "");
            line = Regex.Replace(line, @"[`*_~]+", "");
            line = Regex.Replace(line, @"^\s*\d+(?:\.\d+)+\s+", "");
            line = Regex.Replace(line, @"\s+", " ").Trim();
            foreach (var fragment in Regex.Split(line, "(?<=[.!?])\\s+(?=[\\p{Lu}\\\"“])|(?<=[.!?])$"))
            {
                var sentence = fragment.Trim();
                if (sentence.Length >= 18 && Regex.IsMatch(sentence, @"[\p{L}]{3,}"))
                    yield return char.ToUpperInvariant(sentence[0]) + sentence[1..];
            }
        }
    }

    private static string[] Terms(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
        .Select(match => match.Value.Normalize(NormalizationForm.FormD))
        .Select(term => new string(term.Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()))
        .Select(term => term.Length > 5 && term.EndsWith("es", StringComparison.Ordinal) ? term[..^2] : term.Length > 4 && term.EndsWith('s') ? term[..^1] : term)
        .ToArray();

    public async IAsyncEnumerable<string> StreamAsync(string question, IReadOnlyList<SearchHit> context, string language = "es", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var answer = await GenerateAsync(question, context, language, cancellationToken);
        foreach (var token in Regex.Matches(answer, @"\S+\s*").Select(match => match.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return token;
        }
    }
}
