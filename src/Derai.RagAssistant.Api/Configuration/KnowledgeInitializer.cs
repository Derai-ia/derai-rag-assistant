using Derai.RagAssistant.Application;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;

namespace Derai.RagAssistant.Api.Configuration;

public static class KnowledgeInitializer
{
    public static async Task InitializeKnowledgeAsync(this WebApplication app)
    {
        var services = app.Services;

        // Force named-client pipeline creation so invalid resilience options fail before serving requests.
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        _ = httpClientFactory.CreateClient("llm");
        _ = httpClientFactory.CreateClient("azure-search");
        _ = services.GetRequiredService<IChatModel>();
        _ = services.GetRequiredService<IEmbeddingModel>();

        var stores = services.GetRequiredService<CorpusStores>();
        var loader = services.GetRequiredService<FileKnowledgeLoader>();
        await stores.Get("nordia").IndexAsync(loader.Load("nordia"));
    }
}
