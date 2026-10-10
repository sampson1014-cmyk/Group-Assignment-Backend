using System.Net.Http.Json;
using System.Text.Json;
using LoyaltyBackend.Models;

namespace LoyaltyBackend.Services;

public interface ILoyaltyMemberService
{
    Task<MemberDetailsResponse> GetDetailsAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<MemberSummary> GetSummaryAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<MemberSummary> GetWalletAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityRecord>> GetActivityAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class LoyaltyMemberService(ILoyaltyApiClient apiClient) : ILoyaltyMemberService
{
    public async Task<MemberDetailsResponse> GetDetailsAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.PostAsync(
            "api/MemberDetails/GetMemberDetails",
            new PhoneNumberRequest(phoneNumber),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MemberDetailsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The Loyalty API returned an empty member profile.");
    }

    public async Task<MemberSummary> GetSummaryAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var memberTask = GetDetailsAsync(phoneNumber, cancellationToken);
        var walletTask = GetWalletAsync(phoneNumber, cancellationToken);
        await Task.WhenAll(memberTask, walletTask);
        var member = memberTask.Result;
        var wallet = walletTask.Result;

        return new MemberSummary(
            member.Name ?? "Member",
            member.UserId ?? phoneNumber,
            wallet.Tier,
            wallet.WalletBalance,
            wallet.Points,
            wallet.Stamps,
            member.ReferralCode ?? string.Empty,
            wallet.PointsExpireAt,
            ResolvePhotoSource(member));
    }

    public async Task<MemberSummary> GetWalletAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.PostAsync(
            "api/MemberWallet/MemberGetWalletDetails",
            new PhoneNumberRequest(phoneNumber),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var wallet = await response.Content.ReadFromJsonAsync<WalletDetailsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The Loyalty API returned an empty wallet.");

        return new MemberSummary(
            "Member",
            wallet.UserId ?? phoneNumber,
            wallet.Tier ?? "Member",
            wallet.Balance,
            wallet.Point ?? 0,
            wallet.TotalStamp,
            string.Empty,
            wallet.ExpireDate);
    }

    public async Task<IReadOnlyList<ActivityRecord>> GetActivityAsync(
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.PostAsync(
            "api/History/GetAllRecordByPhoneNumber",
            new PhoneNumberRequest(phoneNumber),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content) || content.Trim() == "[]")
        {
            return Array.Empty<ActivityRecord>();
        }

        var records = JsonSerializer.Deserialize<List<HistoryRecordResponse>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return records
            .OrderByDescending(record => record.OccurredAt)
            .Select(record => new ActivityRecord(
                record.OccurredAt,
                NormalizeType(record.Type),
                string.IsNullOrWhiteSpace(record.Description) ? NormalizeType(record.Type) : record.Description,
                record.Amount == 0 ? null : SignedAmount(record.Type, record.Amount),
                record.Point == 0 ? null : record.Point,
                record.HistoryId ?? record.ReferenceNumber,
                record.Status))
            .ToArray();
    }

    private static string NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Activity";
        if (type.Contains("top", StringComparison.OrdinalIgnoreCase)) return "Top-up";
        if (type.Contains("spend", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("payment", StringComparison.OrdinalIgnoreCase)) return "Wallet";
        if (type.Contains("point", StringComparison.OrdinalIgnoreCase)) return "Points";
        if (type.Contains("stamp", StringComparison.OrdinalIgnoreCase)) return "Stamp";
        if (type.Contains("reward", StringComparison.OrdinalIgnoreCase)) return "Reward";
        if (type.Contains("voucher", StringComparison.OrdinalIgnoreCase)) return "Voucher";
        return type;
    }

    private static decimal SignedAmount(string? type, decimal amount) =>
        type?.Contains("spend", StringComparison.OrdinalIgnoreCase) == true ||
        type?.Contains("payment", StringComparison.OrdinalIgnoreCase) == true
            ? -Math.Abs(amount)
            : amount;

    private static string? ResolvePhotoSource(MemberDetailsResponse member)
    {
        if (!string.IsNullOrWhiteSpace(member.ImageByte))
        {
            return member.ImageByte.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? member.ImageByte
                : $"data:image/jpeg;base64,{member.ImageByte}";
        }

        return Uri.TryCreate(member.Image, UriKind.Absolute, out var imageUri) &&
               imageUri.Scheme is "http" or "https"
            ? imageUri.ToString()
            : null;
    }
}
