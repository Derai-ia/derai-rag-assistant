using Derai.RagAssistant.Api.Configuration;
using Derai.RagAssistant.Api.Endpoints;
using Derai.RagAssistant.Api.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, logger) =>
    logger.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
builder.Services.AddApiServices(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseApiPipeline();
await app.InitializeKnowledgeAsync();
app.MapApiEndpoints();

app.Run();

public partial class Program { }
