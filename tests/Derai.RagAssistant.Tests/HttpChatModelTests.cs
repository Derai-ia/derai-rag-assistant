using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using Xunit;

namespace Derai.RagAssistant.Tests;

public sealed class HttpChatModelTests
{
    [Fact]
    public async Task Streaming_response_can_outlast_total_request_timeout()
    {
        const string body = "data: {\"choices\":[{\"delta\":{\"content\":\"complete stream\"}}]}\n\ndata: [DONE]\n\n";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("stream-test")
            .ConfigurePrimaryHttpMessageHandler(() => new StreamingHandler(body))
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(2);
                options.Retry.MaxRetryAttempts = 1;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("stream-test");
        var model = new HttpChatModel(client, new("OpenAICompatible", "test-key", "https://mock.local/v1/chat/completions", "test-model"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var answer = await model.GenerateAsync("question", [], "en", timeout.Token);

        Assert.Equal("complete stream", answer);
    }

    [Fact]
    public async Task OpenAI_compatible_stream_ignores_empty_choices_events()
    {
        const string stream = "data: {\"choices\":[],\"usage\":{\"completion_tokens\":12}}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"usable answer\"}}]}\n\ndata: [DONE]\n\n";
        using var client = new HttpClient(new CapturingHandler(stream));
        var model = new HttpChatModel(client, new("OpenAICompatible", "test-key", "https://mock.local/v1/chat/completions", "test-model"));

        var answer = await model.GenerateAsync("question", [], "en");

        Assert.Equal("usable answer", answer);
    }

    [Theory]
    [InlineData("OpenAI", "https://mock.local/v1/chat/completions", "Authorization", "Bearer test-key")]
    [InlineData("OpenAICompatible", "https://mock.local/v1/chat/completions", "Authorization", "Bearer test-key")]
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
        Assert.Contains("Answer the question directly first", handler.Body, StringComparison.Ordinal);
        Assert.Contains("do not repeat the question", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same language as the question", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("after every factual claim", handler.Body, StringComparison.OrdinalIgnoreCase);
        if (provider == "OpenAICompatible")
        {
            Assert.Contains("\"temperature\":0.2", handler.Body);
            Assert.Contains("\"max_tokens\":1200", handler.Body);
            Assert.Contains("test-model", handler.Body);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Provider_errors_do_not_expose_api_key(HttpStatusCode status)
    {
        using var client = new HttpClient(new StatusHandler(status));
        var model = new HttpChatModel(client, new("OpenAICompatible", "top-secret-test-key", "https://mock.local/v1/chat/completions", "test-model"));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => model.GenerateAsync("question", [], "en"));
        Assert.DoesNotContain("top-secret-test-key", error.ToString(), StringComparison.Ordinal);
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

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class StreamingHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedStream(Encoding.UTF8.GetBytes(responseBody)))
            });
    }

    private sealed class DelayedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            return await _inner.ReadAsync(buffer[..Math.Min(8, buffer.Length)], cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 8));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
