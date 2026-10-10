using System.Text.RegularExpressions;

namespace LoyaltyBackend.Services;

public static class MemberPhoneIdentity
{
    public static bool IsValid(string? phoneNumber) =>
        phoneNumber is not null && Regex.IsMatch(phoneNumber, @"^\+?\d{8,15}$");
}
