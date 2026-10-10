using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LoyaltyBackend.Services;

public sealed class LoyaltyApiOptions
{
    public const string SectionName = "LoyaltyApi";
    public string BaseUrl { get; set; } = string.Empty;
    public string JwtUserName { get; set; } = string.Empty;
    public string JwtPassword { get; set; } = string.Empty;
}

public interface ILoyaltyApiClient
{
    Task<HttpResponseMessage> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default);
}

public sealed class LoyaltyApiClient : ILoyaltyApiClient
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new() { PropertyNamingPolicy = null };
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public LoyaltyApiClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("SharedGateway");
        var baseUrl = configuration["SharedGateway:BaseUrl"] ?? "http://localhost:3000";
        _httpClient.BaseAddress = Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
            ? baseAddress
            : throw new InvalidOperationException("SharedGateway:BaseUrl must be configured.");
    }

    public async Task<HttpResponseMessage> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "web/member-api")
        {
            Content = JsonContent.Create(new { endpoint = Normalize(endpoint), method = "POST", payload }, options: ApiJsonOptions)
        };
        return await SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "web/member-api")
        {
            Content = JsonContent.Create(new { endpoint = Normalize(endpoint), method = "GET" })
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var secret = _configuration["SharedGateway:SessionSecret"] ?? _configuration["APP_JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("The shared backend session secret is not configured on the web server.");
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(new { role = "web-service", exp = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds() }));
        var unsigned = $"{header}.{body}";
        var token = $"{unsigned}.{Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(unsigned)))}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static string Normalize(string endpoint)
    {
        var path = endpoint.TrimStart('/');
        return path.StartsWith("api/", StringComparison.Ordinal) ? path[4..] : path;
    }
}
