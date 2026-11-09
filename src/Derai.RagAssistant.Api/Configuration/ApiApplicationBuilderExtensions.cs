namespace Derai.RagAssistant.Api.Configuration;

public static class ApiApplicationBuilderExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
