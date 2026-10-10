using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LoyaltyBackend.Services;

// The provider can return a profile without rejecting an incorrect OTP.
// Always verify the server-held challenge before calling a completion endpoint.
public static class VerificationChallenge
{
    public static void Store(ISession session, string key, string code)
    {
        session.SetString(key, code);
        session.SetString(key + ".Expires", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        session.SetInt32(key + ".Attempts", 0);
    }

    public static bool Matches(ISession session, string key, string? supplied)
    {
        var attempts = session.GetInt32(key + ".Attempts") ?? 0;
        _ = long.TryParse(session.GetString(key + ".Expires"), out var expires);
        var valid = attempts < 5 && IsValid(session.GetString(key), supplied, expires, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (!valid) session.SetInt32(key + ".Attempts", attempts + 1);
        return valid;
    }

    public static bool IsValid(string? expected, string? supplied, long expires, long now) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(supplied) && expires > now &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied.Trim()));

    public static void Clear(ISession session, string key)
    {
        session.Remove(key);
        session.Remove(key + ".Expires");
        session.Remove(key + ".Attempts");
    }
}
