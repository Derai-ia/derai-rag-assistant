using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace Derai.RagAssistant.IntegrationTests;

public sealed class ProviderStartupTests
{
    [Theory]
    [InlineData("Fake")]
    [InlineData("OpenAI")]
    [InlineData("AzureOpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Ollama")]
    [InlineData("OpenAICompatible")]
    public async Task Host_starts_and_validates_resilience_for_each_chat_provider(string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider,
            ["Llm:ApiKey"] = "fake-test-key",
            ["Llm:Endpoint"] = provider switch
            {
                "AzureOpenAI" => "https://mock.local/openai/deployments/demo/chat/completions?api-version=2024-10-21",
                "Anthropic" => "https://mock.local/v1/messages",
                "Ollama" => "http://mock.local/api/chat",
                _ => "https://mock.local/v1/chat/completions"
            },
            ["Llm:Model"] = "fake-model",
            ["Llm:OpenAICompatible:ApiKey"] = "fake-compatible-key",
            ["Llm:OpenAICompatible:BaseUrl"] = "https://mock.local/v1",
            ["Llm:OpenAICompatible:ChatModel"] = "fake-model",
            ["Llm:EmbeddingProvider"] = "Fake",
            ["VectorStore:Provider"] = "Memory",
            ["Llm:Resilience:AttemptTimeoutSeconds"] = "45",
            ["Llm:Resilience:TotalRequestTimeoutSeconds"] = "30",
            ["Llm:Resilience:CircuitBreakerSamplingDurationSeconds"] = "30"
        };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Host_starts_with_Azure_search_and_its_resilience_pipeline()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = "Fake",
            ["VectorStore:Provider"] = "AzureAiSearch",
            ["VectorStore:Endpoint"] = "https://mock-search.local",
            ["VectorStore:ApiKey"] = "fake-search-key",
            ["Llm:Resilience:AttemptTimeoutSeconds"] = "45",
            ["Llm:Resilience:TotalRequestTimeoutSeconds"] = "30",
            ["Llm:Resilience:CircuitBreakerSamplingDurationSeconds"] = "30"
        };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            web.ConfigureServices(services => services.AddHttpClient("azure-search")
                .ConfigurePrimaryHttpMessageHandler(() => new SearchHandler()));
        });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        response.EnsureSuccessStatusCode();
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
