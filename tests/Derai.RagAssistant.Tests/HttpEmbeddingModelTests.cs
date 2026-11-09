using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Derai.RagAssistant.Infrastructure;
using Xunit;

namespace Derai.RagAssistant.Tests;

public sealed class HttpEmbeddingModelTests
{
    [Fact]
    public async Task OpenAI_compatible_embeddings_send_distinct_passage_and_query_input_types()
    {
        var handler = new EmbeddingHandler("{\"data\":[{\"embedding\":[0.3,0.4,0.5]}]}");
        using var client = new HttpClient(handler);
        var model = new HttpEmbeddingModel(client, new("OpenAICompatible", "test-key", "https://mock.local/v1/embeddings", "test-embedding-model"));

        await model.EmbedAsync("index text", "passage");
        using var passage = JsonDocument.Parse(handler.Body);
        Assert.Equal("passage", passage.RootElement.GetProperty("input_type").GetString());
        Assert.Equal("END", passage.RootElement.GetProperty("truncate").GetString());

        await model.EmbedAsync("search text", "query");
        using var query = JsonDocument.Parse(handler.Body);
        Assert.Equal("query", query.RootElement.GetProperty("input_type").GetString());
        Assert.Equal("OpenAICompatible:test-embedding-model:3", model.ModelId);
    }

    [Theory]
    [InlineData("OpenAI", "https://mock.local/v1/embeddings", "{\"data\":[{\"embedding\":[0.3,0.4,0.5]}]}")]
    [InlineData("OpenAICompatible", "https://mock.local/v1/embeddings", "{\"data\":[{\"embedding\":[0.3,0.4,0.5]}]}")]
    [InlineData("AzureOpenAI", "https://mock.local/embeddings?api-version=2024-10-21", "{\"data\":[{\"embedding\":[0.3,0.4,0.5]}]}")]
    [InlineData("Ollama", "http://mock.local/api/embed", "{\"embeddings\":[[0.3,0.4,0.5]]}")]
    public async Task Embedding_adapters_parse_provider_payload_and_return_fixed_vector(string provider, string endpoint, string payload)
    {
        var handler = new EmbeddingHandler(payload); using var client = new HttpClient(handler);
        var model = new HttpEmbeddingModel(client, new(provider, "test-key", endpoint, "test-embedding-model"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var vector = await model.EmbedAsync("authorized text", provider == "OpenAICompatible" ? "query" : "passage", timeout.Token);
        Assert.Equal(provider == "OpenAICompatible" ? 3 : 128, vector.Length);
        Assert.Equal(endpoint, handler.Uri?.ToString());
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("authorized text", body.RootElement.GetProperty("input").GetString());
        if (provider == "OpenAICompatible")
        {
            Assert.Equal("query", body.RootElement.GetProperty("input_type").GetString());
            Assert.Equal("END", body.RootElement.GetProperty("truncate").GetString());
            Assert.Equal("Bearer test-key", handler.Authorization);
            Assert.Equal("OpenAICompatible:test-embedding-model:3", model.ModelId);
        }
        Assert.True(Math.Abs(Math.Sqrt(vector.Sum(x => x * x)) - 1) < 0.0001);
        if (provider == "AzureOpenAI") Assert.Equal("test-key", handler.ApiKey);
    }

    private sealed class EmbeddingHandler(string payload) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = "";
        public string? ApiKey { get; private set; }
        public string? Authorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(JsonDocument.Parse(payload).RootElement) };
        }
    }
}
