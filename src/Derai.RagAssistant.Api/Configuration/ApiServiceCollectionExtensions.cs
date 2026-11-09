using Derai.RagAssistant.Application;
using Derai.RagAssistant.Api.Security;
using Derai.RagAssistant.Api.Services;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using System.Security.Cryptography;

namespace Derai.RagAssistant.Api.Configuration;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(configuration["Auth:SigningKey"]))
            configuration["Auth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddRateLimiter(options =>
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = 50;
                limiter.Window = TimeSpan.FromMinutes(1);
            }));
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(configuration["Cors:Origin"] ?? "http://localhost:8080")
                .AllowAnyHeader()
                .AllowAnyMethod()));

        services.AddHttpClient("llm")
            .AddStandardResilienceHandler(options => ConfigureResilience(options, configuration));
        services.AddHttpClient("azure-search")
            .AddStandardResilienceHandler(options => ConfigureResilience(options, configuration));

        services.AddSingleton<IEmbeddingModel>(provider =>
            CreateEmbeddingModel(provider, configuration));
        services.AddSingleton<CorpusStores>(provider =>
            CreateCorpusStores(provider, configuration));
        services.AddSingleton<IChatModel>(provider =>
            CreateChatModel(provider, configuration));
        services.AddSingleton<FileKnowledgeLoader>(_ =>
            new FileKnowledgeLoader(FindDocumentsPath(environment.ContentRootPath)));
        services.AddSingleton<ChatService>();
        services.AddSingleton<IValidator<ChatRequest>, ChatRequestValidator>();
        services.AddSingleton<ConversationHistory>();
        services.AddSingleton<AuthTokenService>();

        return services;
    }

    private static IEmbeddingModel CreateEmbeddingModel(IServiceProvider services, IConfiguration configuration)
    {
        var chatProvider = configuration["Llm:Provider"] ?? "Fake";
        var compatibleEmbeddingModel = configuration["Llm:OpenAICompatible:EmbeddingModel"];
        var configuredEmbeddingProvider = configuration["Llm:EmbeddingProvider"];
        var provider = string.IsNullOrWhiteSpace(configuredEmbeddingProvider)
            ? chatProvider == "OpenAICompatible"
                ? string.IsNullOrWhiteSpace(compatibleEmbeddingModel) ? "Fake" : "OpenAICompatible"
                : chatProvider == "Anthropic" ? "Fake" : chatProvider
            : configuredEmbeddingProvider;

        if (provider == "OpenAICompatible" && string.IsNullOrWhiteSpace(compatibleEmbeddingModel))
            provider = "Fake";
        if (provider == "Fake")
            return new HashEmbeddingModel();
        if (provider is not ("OpenAI" or "AzureOpenAI" or "Ollama" or "OpenAICompatible"))
            throw new InvalidOperationException($"Unsupported Llm:EmbeddingProvider '{provider}'.");

        var endpoint = configuration["Llm:EmbeddingEndpoint"];
        if (provider == "OpenAI")
            endpoint ??= "https://api.openai.com/v1/embeddings";
        var model = provider == "OpenAICompatible"
            ? compatibleEmbeddingModel
            : configuration["Llm:EmbeddingModel"];
        if (provider == "OpenAICompatible")
            endpoint ??= $"{(configuration["Llm:OpenAICompatible:BaseUrl"] ?? "https://integrate.api.nvidia.com/v1").TrimEnd('/')}/embeddings";

        return new HttpEmbeddingModel(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("llm"),
            new EmbeddingOptions(
                provider,
                configuration["Llm:OpenAICompatible:ApiKey"]
                    ?? configuration["Llm:EmbeddingApiKey"]
                    ?? configuration["Llm:ApiKey"],
                endpoint,
                model));
    }

    private static CorpusStores CreateCorpusStores(IServiceProvider services, IConfiguration configuration)
    {
        IVectorStore Create(string corpus)
        {
            if (configuration["VectorStore:Provider"] != "AzureAiSearch")
                return new InMemoryVectorStore(services.GetRequiredService<IEmbeddingModel>());

            var endpoint = configuration["VectorStore:Endpoint"]
                ?? throw new InvalidOperationException("VectorStore:Endpoint is required for AzureAiSearch.");
            var key = configuration["VectorStore:ApiKey"]
                ?? throw new InvalidOperationException("VectorStore:ApiKey is required for AzureAiSearch.");
            var embeddings = services.GetRequiredService<IEmbeddingModel>();
            return new AzureAiSearchVectorStore(
                services.GetRequiredService<IHttpClientFactory>().CreateClient("azure-search"),
                new AzureSearchOptions(
                    endpoint,
                    key,
                    $"{configuration["VectorStore:IndexName"] ?? "knowledge"}-{corpus}",
                    embeddings.ModelId),
                embeddings);
        }

        return new CorpusStores(Create("nordia"));
    }

    private static IChatModel CreateChatModel(IServiceProvider services, IConfiguration configuration)
    {
        var provider = configuration["Llm:Provider"] ?? "Fake";
        if (provider == "Fake")
            return new FakeChatModel();
        if (provider is not ("OpenAI" or "AzureOpenAI" or "Anthropic" or "Ollama" or "OpenAICompatible"))
            throw new InvalidOperationException($"Unsupported Llm:Provider '{provider}'.");

        var endpoint = configuration["Llm:Endpoint"];
        var model = configuration["Llm:Model"];
        var apiKey = configuration["Llm:ApiKey"];
        if (provider == "OpenAICompatible")
        {
            var baseUrl = (configuration["Llm:OpenAICompatible:BaseUrl"] ?? "https://integrate.api.nvidia.com/v1").TrimEnd('/');
            endpoint ??= $"{baseUrl}/chat/completions";
            model = configuration["Llm:OpenAICompatible:ChatModel"] ?? "meta/llama-3.2-1b-instruct";
            apiKey = configuration["Llm:OpenAICompatible:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Llm:OpenAICompatible:ApiKey is required for this chat provider.");
        }

        return new HttpChatModel(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("llm"),
            new LlmOptions(
                provider,
                apiKey,
                endpoint,
                model,
                configuration["Llm:Deployment"],
                configuration.GetValue("Llm:MaxTokens", 1200)));
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options, IConfiguration configuration)
    {
        var attemptSeconds = Math.Clamp(configuration.GetValue("Llm:Resilience:AttemptTimeoutSeconds", 45), 1, 3600);
        var retries = Math.Clamp(
            configuration.GetValue("Llm:Resilience:MaxRetries", configuration.GetValue("Llm:MaxRetries", 2)),
            0,
            10);
        var minTotal = checked(attemptSeconds * (retries + 1) + retries * 2 + 1);
        var totalSeconds = Math.Max(
            minTotal,
            configuration.GetValue(
                "Llm:Resilience:TotalRequestTimeoutSeconds",
                configuration.GetValue("Llm:TimeoutSeconds", 180)));
        var samplingSeconds = Math.Max(
            attemptSeconds * 2,
            configuration.GetValue("Llm:Resilience:CircuitBreakerSamplingDurationSeconds", attemptSeconds * 2));

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptSeconds);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(totalSeconds);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(samplingSeconds);
        options.Retry.MaxRetryAttempts = retries;
    }

    private static string FindDocumentsPath(string contentRoot)
    {
        var direct = Path.Combine(contentRoot, "data", "docs");
        if (Directory.Exists(direct))
            return direct;

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "docs");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return direct;
    }
}
