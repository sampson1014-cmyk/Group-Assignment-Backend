using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

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
    private readonly HttpClient _httpClient;
    private readonly ILoyaltyTokenProvider _tokenProvider;

    public LoyaltyApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LoyaltyApiOptions> options,
        ILoyaltyTokenProvider tokenProvider)
    {
        var settings = options.Value;
        _httpClient = httpClientFactory.CreateClient("LoyaltyApi");
        _httpClient.BaseAddress = Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseAddress)
            ? baseAddress
            : throw new InvalidOperationException("LoyaltyApi:BaseUrl must be configured.");
        _tokenProvider = tokenProvider;
    }

    public async Task<HttpResponseMessage> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Normalize(endpoint))
        {
            Content = JsonContent.Create(payload)
        };
        return await SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Normalize(endpoint));
        return await SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static string Normalize(string endpoint) => endpoint.TrimStart('/');
}
