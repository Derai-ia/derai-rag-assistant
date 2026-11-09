using Derai.RagAssistant.Api.Security;

namespace Derai.RagAssistant.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/users", () => new[]
        {
            new { id = "direccion", label = "DirecciÃ³n", role = "direccion" },
            new { id = "operaciones", label = "Operaciones", role = "operaciones" },
            new { id = "soporte", label = "Soporte", role = "soporte" }
        });

        endpoints.MapPost("/api/v1/auth/demo", (DemoAuth request, AuthTokenService tokens) =>
        {
            if (!new[] { "direccion", "operaciones", "soporte" }.Contains(request.Role))
                return Results.BadRequest();
            return Results.Ok(new { token = tokens.CreateToken(request.Role) });
        });

        return endpoints;
    }
}
