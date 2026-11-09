using System.Text;
using System.Text.Json;
using Derai.RagAssistant.Api.Security;
using Derai.RagAssistant.Api.Services;
using Derai.RagAssistant.Application;
using Derai.RagAssistant.Domain;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;

namespace Derai.RagAssistant.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/chat/sync", async (
            HttpRequest http,
            ChatRequest request,
            string? corpus,
            IValidator<ChatRequest> validator,
            ChatService chat,
            ConversationHistory history,
            AuthTokenService tokens,
            CancellationToken cancellationToken) =>
        {
            var role = tokens.GetRole(http);
            if (role is null)
                return Results.Unauthorized();
            corpus ??= "nordia";
            if (corpus != "nordia")
                return Results.BadRequest();
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());
            history.Add(request.ConversationId, request.Question);
            return Results.Ok(await chat.AskAsync(request, role, cancellationToken, corpus));
        }).RequireRateLimiting("api");

        endpoints.MapPost("/api/v1/chat", async (
            HttpRequest http,
            ChatRequest request,
            string? corpus,
            HttpResponse response,
            IValidator<ChatRequest> validator,
            CorpusStores vectorStores,
            IChatModel model,
            AuthTokenService tokens,
            CancellationToken cancellationToken) =>
        {
            corpus ??= "nordia";
            if (corpus != "nordia")
            {
                response.StatusCode = 400;
                return;
            }

            var role = tokens.GetRole(http);
            if (role is null)
            {
                response.StatusCode = 401;
                return;
            }

            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                response.StatusCode = 400;
                return;
            }

            var hits = await vectorStores.Get(corpus).SearchAsync(
                request.Question, role, 5, cancellationToken, request.Language);
            response.ContentType = "text/event-stream";
            var answer = new StringBuilder();
            await foreach (var token in model.StreamAsync(request.Question, hits, request.Language, cancellationToken))
            {
                answer.Append(token);
                await response.WriteAsync($"event: token\ndata: {JsonSerializer.Serialize(token)}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }

            var answerText = answer.ToString();
            var hasEvidence = hits.Count > 0
                && !answerText.StartsWith("No tengo suficiente información", StringComparison.Ordinal)
                && !answerText.StartsWith("I don't have enough evidence", StringComparison.Ordinal)
                && !answerText.StartsWith("No encuentro", StringComparison.Ordinal)
                && !answerText.StartsWith("I could not find enough information", StringComparison.Ordinal);
            var sources = hasEvidence
                ? hits.Select((hit, index) => new ChatSource(
                    index + 1,
                    hit.Chunk.File,
                    hit.Chunk.Section,
                    hit.Chunk.Text.Length > 360 ? hit.Chunk.Text[..360] + "â€¦" : hit.Chunk.Text,
                    Math.Round(hit.Score, 3),
                    hit.Chunk.Language)).ToArray()
                : [];
            await response.WriteAsync(
                $"event: sources\ndata: {JsonSerializer.Serialize(sources, JsonSerializerOptions.Web)}\n\n",
                cancellationToken);
            await response.WriteAsync("event: done\ndata: {}\n\n", cancellationToken);
        });

        return endpoints;
    }
}
