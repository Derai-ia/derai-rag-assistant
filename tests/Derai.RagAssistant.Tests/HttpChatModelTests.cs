using System.Net;
using System.Text;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using Xunit;

namespace Derai.RagAssistant.Tests;

public sealed class HttpChatModelTests
{
    [Theory]
    [InlineData("OpenAI", "https://mock.local/v1/chat/completions", "Authorization", "Bearer test-key")]
    [InlineData("AzureOpenAI", "https://mock.local/openai/deployments/demo/chat/completions?api-version=2024-10-21", "api-key", "test-key")]
    [InlineData("Anthropic", "https://mock.local/v1/messages", "x-api-key", "test-key")]
    [InlineData("Ollama", "http://mock.local/api/chat", "", "")]
    public async Task Adapter_formats_request_and_parses_stream(string provider, string endpoint, string header, string expectedHeader)
    {
        const string openAi = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}\n\ndata: [DONE]\n\n";
        const string anthropic = "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello world\"}}\n\ndata: [DONE]\n\n";
        const string ollama = "{\"message\":{\"content\":\"Hello world\"},\"done\":false}\n{\"done\":true}\n";
        var handler = new CapturingHandler(provider == "Anthropic" ? anthropic : provider == "Ollama" ? ollama : openAi);
        using var client = new HttpClient(handler);
        var model = new HttpChatModel(client, new(provider, "test-key", endpoint, "test-model"));
        var context = new[] { new SearchHit(new KnowledgeChunk("allowed.md", "Section", "Authorized fact", "en", ["support"]), .03) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = await model.GenerateAsync("question", context, "en", timeout.Token);
        Assert.Equal("Hello world", output);
        Assert.Equal(endpoint, handler.Uri?.ToString());
        if (header.Length > 0) Assert.Contains(expectedHeader, handler.Headers ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CONTEXT", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorized fact", handler.Body);
        Assert.Contains("stream", handler.Body);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = "";
        public string[]? Headers { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Headers = request.Headers.Select(x => $"{x.Key}: {string.Join(',', x.Value)}").Concat(request.Headers.SelectMany(x => x.Value)).ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream") };
        }
    }
}
