using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LoyaltyBackend.Services;

// The MVC server requests the same short-lived QR grants used by mobile/POS.
// Neither the application signing key nor its service session reaches the browser.
public sealed class SharedGatewayQrService(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<string> CreateAsync(string phoneNumber, string? rewardId = null, bool voucher = false, CancellationToken cancellationToken = default)
    {
        var secret = configuration["SharedGateway:SessionSecret"] ?? configuration["APP_JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Configure the shared backend session secret on the web server before generating QR codes.");
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(new { phoneNumber, role = "member", exp = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds() }));
        var unsigned = $"{header}.{body}";
        var signature = Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(unsigned)));
        var baseUrl = configuration["SharedGateway:BaseUrl"] ?? "http://localhost:3000";
        var path = $"members/{Uri.EscapeDataString(phoneNumber)}";
        path += rewardId is null ? "/qr" : $"/rewards/{Uri.EscapeDataString(rewardId)}/qr?kind={(voucher ? "voucher" : "reward")}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{unsigned}.{signature}");
        using var response = await clients.CreateClient("SharedGateway").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("The shared backend could not create an eligible QR code.", null, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("qrToken").GetString()
            ?? throw new InvalidOperationException("The shared backend returned an empty QR code.");
    }
}
