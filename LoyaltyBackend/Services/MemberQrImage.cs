using System.Text.Json;
using QRCoder;

namespace LoyaltyBackend.Services;

public static class MemberQrImage
{
    // Match mobile: raw member grant, compact reward envelope, M/L correction,
    // dark modules on white, and the standard four-module quiet zone.
    public static string RewardPayload(string phoneNumber, string rewardId, bool voucher, string token) =>
        JsonSerializer.Serialize(new { t = "R", p = phoneNumber, r = rewardId, k = voucher ? "v" : "r", v = token });

    public static byte[] Render(string payload, bool reward = false)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, reward ? QRCodeGenerator.ECCLevel.L : QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        // Integer pixels per module keep the source crisp; never crop the quiet zone.
        return qr.GetGraphic(12, [20, 22, 21, 255], [255, 255, 255, 255], drawQuietZones: true);
    }
}
