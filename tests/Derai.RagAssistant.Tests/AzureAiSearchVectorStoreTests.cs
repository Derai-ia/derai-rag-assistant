using System.Net;
using System.Text;
using System.Text.Json;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using Xunit;

namespace Derai.RagAssistant.Tests;

public sealed class AzureAiSearchVectorStoreTests
{
    [Fact]
    public async Task Search_falls_back_to_other_language_when_preferred_language_has_no_hits()
    {
        var handler = new LanguageFallbackHandler();
        using var client = new HttpClient(handler);
        var store = new AzureAiSearchVectorStore(client, new("https://mock.search.windows.net", "unit-key", "demo"), new HashEmbeddingModel());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var hits = await store.SearchAsync("delivery window", "support", 5, timeout.Token, "es");

        Assert.Single(hits);
        Assert.Equal("en", hits[0].Chunk.Language);
        Assert.Equal(2, handler.Filters.Count);
        Assert.Contains("language eq 'es'", handler.Filters[0]);
        Assert.DoesNotContain("language eq", handler.Filters[1]);
    }

    [Fact]
    public async Task Index_creates_schema_and_upserts_then_search_prefilters_and_checks_acl()
    {
        var handler = new SearchHandler(); using var client = new HttpClient(handler);
        var store = new AzureAiSearchVectorStore(client, new("https://mock.search.windows.net", "unit-key", "demo"), new HashEmbeddingModel());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await store.IndexAsync([new KnowledgeChunk("safe.md", "Allowed", "authorized text", "en", ["support"]), new KnowledgeChunk("private.md", "Secret", "private text", "en", ["director"])], timeout.Token);
        var hits = await store.SearchAsync("authorized", "support", 5, timeout.Token, "en");
        Assert.Single(hits);
        Assert.Equal("safe.md", hits[0].Chunk.File);
        Assert.Contains(handler.Requests, x => x.Method == "PUT" && x.Body.Contains("\"dimensions\":128", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, x => x.Body.Contains("@search.action", StringComparison.Ordinal) && x.Body.Contains("mergeOrUpload", StringComparison.Ordinal));
        using var searchRequest = JsonDocument.Parse(handler.Requests.Last().Body);
        Assert.Equal("allowedRoles/any(r: r eq 'support') and language eq 'en'", searchRequest.RootElement.GetProperty("filter").GetString());
        Assert.Equal("preFilter", searchRequest.RootElement.GetProperty("vectorFilterMode").GetString());
        Assert.All(handler.Requests, x => Assert.Equal("unit-key", x.ApiKey));
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        public List<(string Method, string Body, string? ApiKey)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, body, request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null));
            var response = request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.Created) :
                request.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[{\"id\":\"x\",\"file\":\"safe.md\",\"section\":\"Allowed\",\"text\":\"authorized text\",\"language\":\"en\",\"allowedRoles\":[\"support\"],\"@search.score\":0.04},{\"id\":\"y\",\"file\":\"private.md\",\"section\":\"Secret\",\"text\":\"private text\",\"language\":\"en\",\"allowedRoles\":[\"director\"],\"@search.score\":0.9}]}", Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.OK);
            return response;
        }
    }

    private sealed class LanguageFallbackHandler : HttpMessageHandler
    {
        public List<string> Filters { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.Created);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var filter = body.RootElement.GetProperty("filter").GetString()!;
            Filters.Add(filter);
            var result = filter.Contains("language eq 'es'", StringComparison.Ordinal)
                ? "{\"value\":[]}"
                : "{\"value\":[{\"id\":\"x\",\"file\":\"faq.md\",\"section\":\"Tracking\",\"text\":\"Delivery tracking\",\"language\":\"en\",\"allowedRoles\":[\"support\"],\"@search.score\":0.04}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result, Encoding.UTF8, "application/json") };
        }
    }
}
