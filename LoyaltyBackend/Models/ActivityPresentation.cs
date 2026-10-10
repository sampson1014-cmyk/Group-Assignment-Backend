using System.Globalization;

namespace LoyaltyBackend.Models;

// Display-only labels. Provider records and transaction values remain unchanged.
public static class ActivityPresentation
{
    public static string Title(ActivityRecord item)
    {
        var type = item.Type ?? string.Empty;
        if (type.Contains("stamp", StringComparison.OrdinalIgnoreCase))
            return item.Stamps < 0 ? "Stamp redemption" : "Stamp credit";
        if (type.Contains("voucher", StringComparison.OrdinalIgnoreCase)) return "Voucher activity";
        if (type.Contains("reward", StringComparison.OrdinalIgnoreCase)) return "Reward activity";
        if (type.Contains("point", StringComparison.OrdinalIgnoreCase))
            return item.Points < 0 ? "Points redemption" : "Points credit";
        if (type.Contains("top", StringComparison.OrdinalIgnoreCase)) return "Wallet top-up";
        if (type.Contains("payment", StringComparison.OrdinalIgnoreCase) || type.Contains("spend", StringComparison.OrdinalIgnoreCase))
            return "Purchase payment";
        return string.IsNullOrWhiteSpace(item.Description) ? (string.IsNullOrWhiteSpace(type) ? "Account activity" : type) : item.Description;
    }

    public static string Value(ActivityRecord item)
    {
        if (item.Stamps is int stamps) return $"{stamps:+0;-0;0} {(Math.Abs((long)stamps) == 1 ? "stamp" : "stamps")}";
        if (item.Amount is decimal amount) return $"{(amount < 0 ? "−" : amount > 0 ? "+" : "")} RM {Math.Abs(amount).ToString("N2", CultureInfo.GetCultureInfo("en-MY"))}";
        if (item.Points is int points) return $"{points:+0;-0;0} {(Math.Abs((long)points) == 1 ? "point" : "points")}";
        return item.Status ?? "Recorded";
    }

    public static string Icon(ActivityRecord item) => item.Stamps is not null ? "✓" : item.Amount is not null ? "↗" : item.Points is not null ? "✦" : "◇";
}
