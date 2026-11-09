namespace Derai.RagAssistant.Api.Endpoints;

public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapChatEndpoints();
        app.MapKnowledgeEndpoints();
        app.MapEvaluationEndpoints();
        return app;
    }
}
