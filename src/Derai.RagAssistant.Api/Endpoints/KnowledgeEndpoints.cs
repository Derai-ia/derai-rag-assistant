using System.Security.Cryptography;
using System.Text;
using Derai.RagAssistant.Application;
using Derai.RagAssistant.Infrastructure;

namespace Derai.RagAssistant.Api.Endpoints;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/knowledge/reindex", async (
            HttpRequest request,
            IConfiguration configuration,
            CorpusStores stores,
            FileKnowledgeLoader loader,
            CancellationToken cancellationToken) =>
        {
            var configuredKey = Encoding.UTF8.GetBytes(configuration["Admin:ApiKey"] ?? "");
            var suppliedKey = Encoding.UTF8.GetBytes(request.Headers["X-Admin-Key"].ToString());
            if (configuredKey.Length == 0
                || suppliedKey.Length != configuredKey.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedKey, configuredKey))
                return Results.Unauthorized();
            var corpus = request.Query["corpus"].FirstOrDefault() ?? "nordia";
            if (corpus != "nordia")
                return Results.BadRequest();

            await stores.Get(corpus).IndexAsync(loader.Load(corpus), cancellationToken);
            return Results.Ok(new { status = "reindexed", corpus });
        });

        return endpoints;
    }
}
