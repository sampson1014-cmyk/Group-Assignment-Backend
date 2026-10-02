using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LoyaltyBackend.Services;

public interface ILoyaltyTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class LoyaltyTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<LoyaltyApiOptions> options) : ILoyaltyTokenProvider
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _refreshAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HasCurrentToken())
        {
            return _token!;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (HasCurrentToken())
            {
                return _token!;
            }

            var settings = options.Value;
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseAddress))
            {
                throw new InvalidOperationException("LoyaltyApi:BaseUrl must be configured.");
            }

            if (string.IsNullOrWhiteSpace(settings.JwtUserName) || string.IsNullOrWhiteSpace(settings.JwtPassword))
            {
                throw new InvalidOperationException("Loyalty API JWT credentials are not configured.");
            }

            var tokenUri = QueryHelpers.AddQueryString(
                new Uri(baseAddress, "api/JWTToken/Post").ToString(),
                new Dictionary<string, string?>
                {
                    ["UserName"] = settings.JwtUserName,
                    ["Password"] = settings.JwtPassword
                });

            var client = httpClientFactory.CreateClient("LoyaltyApi");
            using var response = await client.PostAsync(tokenUri, content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JwtTokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("The Loyalty API returned an empty JWT response.");

            if (string.IsNullOrWhiteSpace(result.SuccessToken))
            {
                throw new InvalidOperationException("The Loyalty API JWT response did not contain SuccessToken.");
            }

            var lifetimeMinutes = double.TryParse(result.TokenMinutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 30;
            _token = result.SuccessToken;
            _refreshAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, lifetimeMinutes - 1));
            return _token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool HasCurrentToken() => !string.IsNullOrWhiteSpace(_token) && DateTimeOffset.UtcNow < _refreshAt;

    private sealed record JwtTokenResponse(
        [property: JsonPropertyName("SuccessToken")] string? SuccessToken,
        [property: JsonPropertyName("TokenMinutes")] string? TokenMinutes,
        [property: JsonPropertyName("TokenRoles")] string? TokenRoles);
}
