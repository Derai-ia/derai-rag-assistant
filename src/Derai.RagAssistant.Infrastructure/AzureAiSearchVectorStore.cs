using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Derai.RagAssistant.Domain;

namespace Derai.RagAssistant.Infrastructure;

public sealed record AzureSearchOptions(string Endpoint, string ApiKey, string IndexName = "derai-knowledge", string EmbeddingModel = "unspecified-embedding-128");

/// <summary>Azure AI Search REST adapter with role prefilters and batched document upserts.</summary>
public sealed class AzureAiSearchVectorStore(HttpClient client, AzureSearchOptions options, IEmbeddingModel embeddings) : IVectorStore
{
    private const string ApiVersion = "2025-09-01";
    private bool _indexReady;
    private string IndexName => options.IndexName + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(embeddings.ModelId))).ToLowerInvariant()[..10];

    public async Task IndexAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
    {
        var items = new List<SearchDocument>(); var index = 0;
        foreach (var chunk in chunks)
        {
            items.Add(new SearchDocument
            {
                Id = MakeId(chunk, index++),
                File = chunk.File,
                Section = chunk.Section,
                Text = chunk.Text,
                Language = chunk.Language,
                AllowedRoles = chunk.AllowedRoles,
                Vector = (await embeddings.EmbedAsync(chunk.Text, "passage", cancellationToken)).Select(x => (double)x).ToArray(),
                EmbeddingModel = embeddings.ModelId
            });
        }
        await EnsureIndexAsync(cancellationToken, items.FirstOrDefault()?.Vector.Length ?? 128);
        foreach (var batch in items.Chunk(500))
        {
            var documents = batch.Select(x => new Dictionary<string, object?> { ["@search.action"] = "mergeOrUpload", ["id"] = x.Id, ["file"] = x.File, ["section"] = x.Section, ["text"] = x.Text, ["language"] = x.Language, ["allowedRoles"] = x.AllowedRoles, ["embeddingModel"] = x.EmbeddingModel, ["embeddingDimension"] = x.Vector.Length, ["vector"] = x.Vector }).ToArray();
            using var response = await SendAsync(HttpMethod.Post, $"indexes/{IndexName}/docs/index?api-version={ApiVersion}", new { value = documents }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string role, int take, CancellationToken cancellationToken = default, string language = "es")
    {
        var roleFilter = role.Replace("'", "''", StringComparison.Ordinal);
        var vector = (await embeddings.EmbedAsync(query, "query", cancellationToken)).Select(x => (double)x).ToArray();
        await EnsureIndexAsync(cancellationToken, vector.Length);
        var preferred = await SearchForLanguageAsync(language);
        return preferred.Count > 0 ? preferred : await SearchForLanguageAsync(null);

        async Task<IReadOnlyList<SearchHit>> SearchForLanguageAsync(string? preferredLanguage)
        {
            var languageFilter = preferredLanguage is null ? "" : $" and language eq '{preferredLanguage.Replace("'", "''", StringComparison.Ordinal)}'";
            using var response = await SendAsync(HttpMethod.Post, $"indexes/{IndexName}/docs/search?api-version={ApiVersion}", new
            {
                search = query,
                vectorQueries = new[] { new { kind = "vector", vector, fields = "vector", k = take } },
                filter = $"allowedRoles/any(r: r eq '{roleFilter}'){languageFilter}",
                vectorFilterMode = "preFilter",
                top = take,
                select = "id,file,section,text,language,allowedRoles,embeddingModel,embeddingDimension"
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var hits = new List<SearchHit>();
            foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                var allowed = item.GetProperty("allowedRoles").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                if (!allowed.Contains(role, StringComparer.OrdinalIgnoreCase)) continue;
                var chunkLanguage = item.GetProperty("language").GetString()!;
                var indexedModel = item.TryGetProperty("embeddingModel", out var modelProperty) ? modelProperty.GetString() : null;
                var indexedDimension = item.TryGetProperty("embeddingDimension", out var dimensionProperty) ? dimensionProperty.GetInt32() : vector.Length;
                if (indexedModel is not null && (!indexedModel.Equals(embeddings.ModelId, StringComparison.Ordinal) || indexedDimension != vector.Length))
                    throw new InvalidOperationException("The search index contains embeddings from an incompatible model or dimension. Reindex this corpus.");
                if (preferredLanguage is not null && !chunkLanguage.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase)) continue;
                var chunk = new KnowledgeChunk(item.GetProperty("file").GetString()!, item.GetProperty("section").GetString()!, item.GetProperty("text").GetString()!, chunkLanguage, allowed);
                hits.Add(new(chunk, item.GetProperty("@search.score").GetDouble()));
            }
            return hits;
        }
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken, int vectorDimensions)
    {
        if (_indexReady) return;
        var schema = new
        {
            name = IndexName,
            fields = new object[]
            {
                new { name="id", type="Edm.String", key=true, searchable=false, filterable=true },
                new { name="file", type="Edm.String", searchable=true, filterable=true, retrievable=true },
                new { name="section", type="Edm.String", searchable=true, retrievable=true },
                new { name="text", type="Edm.String", searchable=true, retrievable=true },
                new { name="language", type="Edm.String", filterable=true, retrievable=true },
                new { name="allowedRoles", type="Collection(Edm.String)", filterable=true, retrievable=true },
                new { name="embeddingModel", type="Edm.String", filterable=true, retrievable=true },
                new { name="embeddingDimension", type="Edm.Int32", filterable=true, retrievable=true },
                new { name="vector", type="Collection(Edm.Single)", searchable=true, retrievable=false, dimensions=vectorDimensions, vectorSearchProfile="derai-hnsw" }
            },
            vectorSearch = new { algorithms = new[] { new { name = "derai-hnsw", kind = "hnsw", parameters = new { metric = "cosine" } } }, profiles = new[] { new { name = "derai-hnsw", algorithm = "derai-hnsw" } } }
        };
        using var response = await SendAsync(HttpMethod.Put, $"indexes/{IndexName}?api-version={ApiVersion}", schema, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
        _indexReady = true;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{options.Endpoint.TrimEnd('/')}/{path}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("api-key", options.ApiKey);
        return await client.SendAsync(request, cancellationToken);
    }

    private static string MakeId(KnowledgeChunk chunk, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{chunk.File}|{chunk.Section}|{index}|{chunk.Text}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class SearchDocument
    {
        public required string Id { get; init; }
        public required string File { get; init; }
        public required string Section { get; init; }
        public required string Text { get; init; }
        public required string Language { get; init; }
        public required string[] AllowedRoles { get; init; }
        public required double[] Vector { get; init; }
        public required string EmbeddingModel { get; init; }
    }
}
