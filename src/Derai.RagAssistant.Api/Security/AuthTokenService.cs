using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Derai.RagAssistant.Api.Security;

public sealed record DemoAuth(string Role);

public sealed class AuthTokenService(IConfiguration configuration)
{
    public string CreateToken(string role)
    {
        var secret = Encoding.UTF8.GetBytes(configuration["Auth:SigningKey"]!);
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = role,
            exp = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds()
        })));
        var body = header + "." + payload;
        return body + "." + Base64Url(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body)));
    }

    public string? GetRole(HttpRequest request)
    {
        var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var secret = Encoding.UTF8.GetBytes(configuration["Auth:SigningKey"]!);
            var body = parts[0] + "." + parts[1];
            var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body));
            if (!CryptographicOperations.FixedTimeEquals(expected, FromBase64Url(parts[2])))
                return null;

            using var json = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = json.RootElement;
            if (root.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;
            return root.GetProperty("sub").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
