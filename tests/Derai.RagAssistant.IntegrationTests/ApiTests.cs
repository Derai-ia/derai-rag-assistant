using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Derai.RagAssistant.IntegrationTests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiTests(WebApplicationFactory<Program> factory) => _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    [Fact]
    public async Task Role_acl_is_enforced_before_response_and_sources()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = _factory.CreateClient();
        var support = await GetToken(client, "soporte", timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/sync") { Content = JsonContent.Create(new { question = "condiciones económicas del contrato tarifas 84 euros", conversationId = "acl-test" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", support);
        using var response = await client.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("84 euros", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contrato-marco-transportista.md", body, StringComparison.OrdinalIgnoreCase);
        var director = await GetToken(client, "direccion", timeout.Token);
        using var authorized = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/sync") { Content = JsonContent.Create(new { question = "84 euros", conversationId = "acl-director", language = "en" }) };
        authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director);
        using var authorizedResponse = await client.SendAsync(authorized, timeout.Token); authorizedResponse.EnsureSuccessStatusCode();
        using var authorizedJson = JsonDocument.Parse(await authorizedResponse.Content.ReadAsStringAsync(timeout.Token));
        Assert.Contains("According to", authorizedJson.RootElement.GetProperty("answer").GetString());
        Assert.Equal("contrato-marco-transportista.md", authorizedJson.RootElement.GetProperty("sources")[0].GetProperty("file").GetString());
        Assert.Contains("[1]", authorizedJson.RootElement.GetProperty("answer").GetString());
    }

    [Theory]
    [InlineData("es", "condiciones económicas confidenciales tarifa negociada 84 euros")]
    [InlineData("en", "confidential commercial terms negotiated rate 84 euros")]
    public async Task Direction_gets_contract_citation_in_requested_language(string language, string question)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = _factory.CreateClient();
        var director = await GetToken(client, "direccion", timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/sync") { Content = JsonContent.Create(new { question, conversationId = $"contract-{language}", language }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director);

        using var response = await client.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var answer = json.RootElement.GetProperty("answer").GetString();
        var source = json.RootElement.GetProperty("sources")[0];

        Assert.Contains(language == "en" ? "According to" : "Según", answer);
        Assert.Contains("[1]", answer);
        Assert.Equal("contrato-marco-transportista.md", source.GetProperty("file").GetString());
        Assert.Equal(language, source.GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("es", "condiciones económicas confidenciales tarifa negociada 84 euros")]
    [InlineData("en", "confidential commercial terms negotiated rate 84 euros")]
    public async Task Support_abstains_from_contract_in_both_languages(string language, string question)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = _factory.CreateClient();
        var support = await GetToken(client, "soporte", timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/sync") { Content = JsonContent.Create(new { question, conversationId = $"support-{language}", language }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", support);

        using var response = await client.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token);

        Assert.DoesNotContain("84 euros", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contrato-marco-transportista.md", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "I could not find enough information" : "No encuentro información suficiente", body);
    }

    [Fact]
    public async Task Health_and_sse_complete_with_done_event()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = _factory.CreateClient();
        using var health = await client.GetAsync("/health/ready", timeout.Token); health.EnsureSuccessStatusCode();
        var token = await GetToken(client, "direccion", timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat") { Content = JsonContent.Create(new { question = "plazo del contrato", conversationId = "sse-test" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains("event: token", body); Assert.Contains("event: sources", body); Assert.Contains("event: done", body);
    }

    private static async Task<string> GetToken(HttpClient client, string role, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/demo", new { role }, token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); return json.RootElement.GetProperty("token").GetString()!;
    }
}
