using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Derai.RagAssistant.Application;
using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using FluentValidation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:SigningKey"]))
    builder.Configuration["Auth:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("api", o => { o.PermitLimit = 50; o.Window = TimeSpan.FromMinutes(1); }));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:8080").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient("llm").AddStandardResilienceHandler();
builder.Services.AddHttpClient("azure-search").AddStandardResilienceHandler();
builder.Services.AddSingleton<IEmbeddingModel>(services =>
{
    var chatProvider = builder.Configuration["Llm:Provider"] ?? "Fake";
    var provider = builder.Configuration["Llm:EmbeddingProvider"] ?? (chatProvider == "Anthropic" ? "Fake" : chatProvider);
    if (provider == "Fake") return new HashEmbeddingModel();
    if (provider is not ("OpenAI" or "AzureOpenAI" or "Ollama")) throw new InvalidOperationException($"Unsupported Llm:EmbeddingProvider '{provider}'.");
    var endpoint = builder.Configuration["Llm:EmbeddingEndpoint"];
    if (provider == "OpenAI") endpoint ??= "https://api.openai.com/v1/embeddings";
    return new HttpEmbeddingModel(services.GetRequiredService<IHttpClientFactory>().CreateClient("llm"), new EmbeddingOptions(provider, builder.Configuration["Llm:EmbeddingApiKey"] ?? builder.Configuration["Llm:ApiKey"], endpoint, builder.Configuration["Llm:EmbeddingModel"]));
});
builder.Services.AddSingleton<IVectorStore>(services =>
{
    if (builder.Configuration["VectorStore:Provider"] != "AzureAiSearch") return new InMemoryVectorStore(services.GetRequiredService<IEmbeddingModel>());
    var endpoint = builder.Configuration["VectorStore:Endpoint"] ?? throw new InvalidOperationException("VectorStore:Endpoint is required for AzureAiSearch.");
    var key = builder.Configuration["VectorStore:ApiKey"] ?? throw new InvalidOperationException("VectorStore:ApiKey is required for AzureAiSearch.");
    return new AzureAiSearchVectorStore(services.GetRequiredService<IHttpClientFactory>().CreateClient("azure-search"), new AzureSearchOptions(endpoint, key, builder.Configuration["VectorStore:IndexName"] ?? "derai-knowledge"), services.GetRequiredService<IEmbeddingModel>());
});
builder.Services.AddSingleton<IChatModel>(services =>
{
    var provider = builder.Configuration["Llm:Provider"] ?? "Fake";
    if (provider == "Fake") return new FakeChatModel();
    if (provider is not ("OpenAI" or "AzureOpenAI" or "Anthropic" or "Ollama")) throw new InvalidOperationException($"Unsupported Llm:Provider '{provider}'.");
    return new HttpChatModel(services.GetRequiredService<IHttpClientFactory>().CreateClient("llm"), new LlmOptions(provider, builder.Configuration["Llm:ApiKey"], builder.Configuration["Llm:Endpoint"], builder.Configuration["Llm:Model"], builder.Configuration["Llm:Deployment"]));
});
builder.Services.AddSingleton<FileKnowledgeLoader>(_ => new(FindDocumentsPath(builder.Environment.ContentRootPath)));
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<IValidator<ChatRequest>, ChatRequestValidator>();
builder.Services.AddSingleton<ConversationHistory>();
var app = builder.Build();
app.UseExceptionHandler(); app.UseRateLimiter(); app.UseCors(); app.UseDefaultFiles(); app.UseStaticFiles(); app.UseSwagger(); app.UseSwaggerUI();
var store = app.Services.GetRequiredService<IVectorStore>();
var loader = app.Services.GetRequiredService<FileKnowledgeLoader>();
await store.IndexAsync(loader.Load());
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
app.MapGet("/api/v1/users", () => new[] { new { id = "direccion", label = "Dirección", role = "direccion" }, new { id = "operaciones", label = "Operaciones", role = "operaciones" }, new { id = "soporte", label = "Soporte", role = "soporte" } });
app.MapPost("/api/v1/auth/demo", (DemoAuth request, IConfiguration config) =>
{
    if (!new[] { "direccion", "operaciones", "soporte" }.Contains(request.Role)) return Results.BadRequest();
    var secret = Encoding.UTF8.GetBytes(config["Auth:SigningKey"]!);
    var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
    var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = request.Role, exp = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds() })));
    var body = header + "." + payload;
    return Results.Ok(new { token = body + "." + Base64Url(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body))) });
});
app.MapPost("/api/v1/chat/sync", async (HttpRequest http, ChatRequest request, IValidator<ChatRequest> validator, ChatService chat, ConversationHistory history, CancellationToken ct) =>
{
    var role = GetRole(http, app.Configuration); if (role is null) return Results.Unauthorized();
    var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    history.Add(request.ConversationId, request.Question); return Results.Ok(await chat.AskAsync(request, role, ct));
}).RequireRateLimiting("api");
app.MapPost("/api/v1/chat", async (HttpRequest http, ChatRequest request, HttpResponse response, IValidator<ChatRequest> validator, IVectorStore vectorStore, IChatModel model, CancellationToken ct) =>
{
    var role = GetRole(http, app.Configuration); if (role is null) { response.StatusCode = 401; return; }
    var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) { response.StatusCode = 400; return; }
    var hits = await vectorStore.SearchAsync(request.Question, role, 5, ct, request.Language); response.ContentType = "text/event-stream";
    var answer = new StringBuilder();
    await foreach (var token in model.StreamAsync(request.Question, hits, request.Language, ct)) { answer.Append(token); await response.WriteAsync($"event: token\ndata: {JsonSerializer.Serialize(token)}\n\n", ct); await response.Body.FlushAsync(ct); }
    var hasEvidence = hits.Count > 0 && !answer.ToString().StartsWith("No encuentro", StringComparison.Ordinal) && !answer.ToString().StartsWith("I could not find enough information", StringComparison.Ordinal);
    var sources = hasEvidence ? hits.Select((hit, index) => new ChatSource(index + 1, hit.Chunk.File, hit.Chunk.Section, hit.Chunk.Text.Length > 360 ? hit.Chunk.Text[..360] + "…" : hit.Chunk.Text, Math.Round(hit.Score, 3), hit.Chunk.Language)).ToArray() : [];
    await response.WriteAsync($"event: sources\ndata: {JsonSerializer.Serialize(sources, JsonSerializerOptions.Web)}\n\n", ct);
    await response.WriteAsync("event: done\ndata: {}\n\n", ct);
});
app.MapPost("/api/v1/knowledge/reindex", async (HttpRequest req, IConfiguration config, CancellationToken ct) =>
{
    var configuredKey = Encoding.UTF8.GetBytes(config["Admin:ApiKey"] ?? "");
    var suppliedKey = Encoding.UTF8.GetBytes(req.Headers["X-Admin-Key"].ToString());
    if (configuredKey.Length == 0 || suppliedKey.Length != configuredKey.Length || !CryptographicOperations.FixedTimeEquals(suppliedKey, configuredKey)) return Results.Unauthorized();
    await store.IndexAsync(loader.Load(), ct); return Results.Ok(new { status = "reindexed" });
});
app.MapGet("/api/v1/eval", async (ChatService chat) =>
{
    var cases = new[]
    {
        ("plazo reclamaciones cinco días", "reclamaciones.md", "soporte", "es"), ("alta proveedores documentación", "alta-proveedores.md", "operaciones", "es"),
        ("crear envío identificador TMS", "manual-tms.md", "operaciones", "es"), ("diferencias inventario almacén", "incidencias-almacen.md", "operaciones", "es"),
        ("seguridad información autenticación multifactor", "seguridad.md", "direccion", "es"), ("tarifas negociadas por palé estándar", "contrato-marco-transportista.md", "direccion", "es"),
        ("vacaciones permisos diez días laborables", "rrhh.md", "direccion", "es"), ("entrega demorada Alba Vega", "caso-01.md", "soporte", "es"),
        ("etiqueta duplicada envío", "caso-02.md", "soporte", "es"), ("penalización transportista retraso", "contrato-marco-transportista.md", "direccion", "es"),
        ("supplier onboarding legal business name", "alta-proveedores.md", "operaciones", "en"), ("duplicate label same parcel", "caso-02.md", "soporte", "en"),
        ("damaged goods package quality", "caso-04.md", "soporte", "en"), ("standard pallet negotiated rates euros", "contrato-marco-transportista.md", "direccion", "en"),
        ("claims procedure business days", "reclamaciones.md", "soporte", "en"), ("warehouse inventory discrepancies units", "incidencias-almacen.md", "operaciones", "en"),
        ("vacation requests business days", "rrhh.md", "direccion", "en"), ("multifactor authentication security", "seguridad.md", "operaciones", "en"),
        ("returns request calendar days shipment", "devoluciones.md", "soporte", "en"), ("tracking shipment TMS six hours", "manual-tms.md", "operaciones", "en")
    };
    var rows = new List<object>(); var hits = 0;
    foreach (var (question, expected, role, language) in cases) { var answer = await chat.AskAsync(new(question, "eval", language), role); var found = answer.Sources.Any(s => s.File.Equals(expected, StringComparison.OrdinalIgnoreCase) && s.Language == language); if (found) hits++; rows.Add(new { question, expected, language, hit = found, role }); }
    return Results.Ok(new { metric = "hit-rate@5", score = (double)hits / cases.Length, hits, total = cases.Length, cases = rows });
});

app.Run();

static string? GetRole(HttpRequest request, IConfiguration config)
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal); var parts = token.Split('.'); if (parts.Length != 3) return null;
    try { var secret = Encoding.UTF8.GetBytes(config["Auth:SigningKey"]!); var body = parts[0] + "." + parts[1]; var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body)); if (!CryptographicOperations.FixedTimeEquals(expected, FromBase64Url(parts[2]))) return null; using var json = JsonDocument.Parse(FromBase64Url(parts[1])); var root = json.RootElement; if (root.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null; return root.GetProperty("sub").GetString(); } catch { return null; }
}
static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
static string FindDocumentsPath(string contentRoot)
{
    var direct = Path.Combine(contentRoot, "data", "docs"); if (Directory.Exists(direct)) return direct;
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    { var candidate = Path.Combine(directory.FullName, "data", "docs"); if (Directory.Exists(candidate)) return candidate; }
    return direct;
}
public sealed record DemoAuth(string Role);
public sealed class ConversationHistory { private readonly Dictionary<string, Queue<string>> _items = new(); public void Add(string id, string question) { if (!_items.TryGetValue(id, out var queue)) _items[id] = queue = new(); queue.Enqueue(question); while (queue.Count > 8) queue.Dequeue(); } }
public partial class Program { }
