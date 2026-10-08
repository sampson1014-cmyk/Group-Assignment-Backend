using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoyaltyBackend.Models;

namespace LoyaltyBackend.Services;

public interface ILoyaltyFeatureService
{
    Task<RewardsViewModel> GetRewardsAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<RewardCard?> GetRewardAsync(string rewardId, string phoneNumber, bool voucher, CancellationToken cancellationToken = default);
    Task<NotificationCard?> GetNotificationAsync(string notificationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityRecord>> GetHistoryAsync(string phoneNumber, string type, CancellationToken cancellationToken = default);
    Task<ActivityRecord?> GetTopUpDetailsAsync(string topUpId, CancellationToken cancellationToken = default);
    Task<StampsViewModel> GetStampsAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationCard>> GetNotificationsAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task MarkNotificationReadAsync(string phoneNumber, string notificationId, CancellationToken cancellationToken = default);
    Task MarkAllNotificationsReadAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutletCard>> GetOutletsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HighlightCard>> GetHighlightsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferralMemberCard>> GetReferralsAsync(string referralCode, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(string phoneNumber, ProfileEditViewModel model, CancellationToken cancellationToken = default);
    Task SubmitFeedbackAsync(string userId, FeedbackViewModel model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackRecordCard>> GetFeedbackAsync(string userId, CancellationToken cancellationToken = default);
    Task DeactivateAccountAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class LoyaltyFeatureService(ILoyaltyApiClient apiClient) : ILoyaltyFeatureService
{
    public async Task<RewardsViewModel> GetRewardsAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var rewardsTask = GetArrayAsync("api/MemberReward/GetRewards", null, cancellationToken);
        var vouchersTask = GetArrayAsync("api/MemberVoucher/GetAllVoucher", null, cancellationToken);
        var ownedRewardsTask = GetArrayAsync("api/MemberAccount/GetMemberReward", new PhoneNumberRequest(phoneNumber), cancellationToken);
        var ownedVouchersTask = GetArrayAsync("api/MemberVoucher/GetVoucherByPhone", new PhoneNumberRequest(phoneNumber), cancellationToken);
        await Task.WhenAll(rewardsTask, vouchersTask, ownedRewardsTask, ownedVouchersTask);

        return new RewardsViewModel
        {
            Rewards = rewardsTask.Result.Select(item => MapReward(item, false, "Reward")).ToArray(),
            Vouchers = vouchersTask.Result.Select(item => MapReward(item, false, "Voucher")).ToArray(),
            OwnedRewards = ownedRewardsTask.Result.Select(item => MapReward(item, true, "Reward")).ToArray(),
            OwnedVouchers = ownedVouchersTask.Result.Select(item => MapReward(item, true, "Voucher")).ToArray()
        };
    }

    public async Task<RewardCard?> GetRewardAsync(
        string rewardId,
        string phoneNumber,
        bool voucher,
        CancellationToken cancellationToken = default)
    {
        var item = await GetObjectAsync(
            voucher ? "api/MemberVoucher/GetVoucherById" : "api/MemberReward/FindReward",
            voucher ? new { RewardId = rewardId, PhoneNumber = phoneNumber } : new { RewardId = rewardId },
            cancellationToken);
        return item is null ? null : MapReward(item.Value, false, voucher ? "Voucher" : "Reward");
    }

    public async Task<NotificationCard?> GetNotificationAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        var item = await GetObjectAsync(
            "api/MemberNotification/GetNotificationsDetails",
            new { Notification_Id = notificationId },
            cancellationToken);
        return item is null ? null : MapNotification(item.Value);
    }

    public async Task<IReadOnlyList<ActivityRecord>> GetHistoryAsync(
        string phoneNumber,
        string type,
        CancellationToken cancellationToken = default)
    {
        var endpoint = type.ToLowerInvariant() switch
        {
            "top-up" => "api/History/GetTopUpRecordByPhoneNumber",
            "wallet" => "api/History/GetPaymentRecordByPhoneNumber",
            "points" => "api/History/GetAssignPointRecordByPhoneNumber",
            "stamp" => "api/History/GetAssignStampRecordByPhoneNumber",
            "reward" => "api/History/GetRedeemRewardRecordByPhoneNumber",
            "voucher" => "api/History/GetRedeemVoucherRecordByPhoneNumber",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var items = await GetArrayAsync(endpoint, new PhoneNumberRequest(phoneNumber), cancellationToken);
        return items.Select(item => MapActivity(item, type)).OrderByDescending(item => item.OccurredAt).ToArray();
    }

    public async Task<ActivityRecord?> GetTopUpDetailsAsync(
        string topUpId,
        CancellationToken cancellationToken = default)
    {
        var item = await GetObjectAsync(
            "api/MemberAccount/GetTopUpRecordDetails",
            new { TopupId = topUpId },
            cancellationToken);
        return item is null ? null : MapActivity(item.Value, "Top-up");
    }

    public async Task<StampsViewModel> GetStampsAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var activeTask = GetArrayAsync("api/MemberAccount/GetMemberStampList", new PhoneNumberRequest(phoneNumber), cancellationToken);
        var usedTask = GetArrayAsync("api/MemberAccount/GetMemberStampUsedRecord", new PhoneNumberRequest(phoneNumber), cancellationToken);
        await Task.WhenAll(activeTask, usedTask);
        return new StampsViewModel
        {
            Active = activeTask.Result.Select(item => MapStamp(item, false)).ToArray(),
            Used = usedTask.Result.Select(item => MapStamp(item, true)).ToArray()
        };
    }

    public async Task<IReadOnlyList<NotificationCard>> GetNotificationsAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var items = await GetArrayAsync("api/MemberNotification/GetNotificationsFilterMember", new { PhoneNumber = phoneNumber }, cancellationToken);
        return items.Select(MapNotification)
            .OrderByDescending(item => item.SentAt)
            .ToArray();
    }

    public async Task MarkNotificationReadAsync(string phoneNumber, string notificationId, CancellationToken cancellationToken = default) =>
        await PostForSuccessAsync("api/MemberNotification/UserReadNotification", new { PhoneNumber = phoneNumber, NotificationId = notificationId }, cancellationToken);

    public async Task MarkAllNotificationsReadAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        await PostForSuccessAsync("api/MemberNotification/UserReadAllNotification", new { PhoneNumber = phoneNumber }, cancellationToken);

    public async Task<IReadOnlyList<OutletCard>> GetOutletsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetArrayAsync("api/ManageOutlets/GetAllOutlets", null, cancellationToken);
        return items.Select(item => new OutletCard(
                Int(item, "Id") ?? 0,
                Text(item, "Name") ?? "Outlet",
                Text(item, "Address") ?? string.Empty,
                Text(item, "PhoneNumber") ?? string.Empty,
                Double(item, "Latitude") ?? 0,
                Double(item, "Longitude") ?? 0))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    public async Task<IReadOnlyList<HighlightCard>> GetHighlightsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetArrayAsync("api/ManageHighlight/UserGetAllHighlight", null, cancellationToken);
        return items.Select(item => new HighlightCard(
            Text(item, "Title") ?? "Member update",
            Text(item, "SubTitle") ?? string.Empty,
            PlainText(item, "Description") ?? string.Empty,
            Text(item, "ExternalLink"),
            Text(item, "ButtonText"),
            Date(item, "ExpireDate"))).ToArray();
    }

    public async Task<IReadOnlyList<ReferralMemberCard>> GetReferralsAsync(string referralCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(referralCode)) return Array.Empty<ReferralMemberCard>();
        var items = await GetArrayAsync("api/MemberAccount/GetMemberDownlineList", new { ReferralCode = referralCode }, cancellationToken);
        return items.Select(item => new ReferralMemberCard(
            Text(item, "Name") ?? "Member",
            Text(item, "Tier") ?? "Member",
            Date(item, "RegisterTime", "CreateDate"))).ToArray();
    }

    public async Task UpdateProfileAsync(string phoneNumber, ProfileEditViewModel model, CancellationToken cancellationToken = default) =>
        await PostForSuccessAsync("api/MemberAccount/MemberEditProfile", new
        {
            PhoneNumber = phoneNumber,
            UserName = model.Name,
            model.Email,
            ImageByte = model.ImageByte ?? string.Empty,
            Birthday = model.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, cancellationToken);

    public async Task<IReadOnlyList<FeedbackRecordCard>> GetFeedbackAsync(string userId, CancellationToken cancellationToken = default)
    {
        var items = await GetArrayAsync("api/FeedBack/GetFeedbackListFilterUser", new { UserId = userId }, cancellationToken);
        return items.Select(item => new FeedbackRecordCard(
            Text(item, "Title") ?? "Feedback",
            PlainText(item, "Description") ?? string.Empty,
            Int(item, "Rating") ?? 0,
            Text(item, "Category") ?? "General",
            Date(item, "CreateDate", "CreatedAt", "DateTime"))).ToArray();
    }

    public async Task SubmitFeedbackAsync(string userId, FeedbackViewModel model, CancellationToken cancellationToken = default) =>
        await PostForSuccessAsync("api/FeedBack/CreateFeedback", new
        {
            model.Title,
            model.Description,
            Rating = model.Rating.ToString(CultureInfo.InvariantCulture),
            model.Category,
            UserId = userId,
            Location = model.Location ?? string.Empty
        }, cancellationToken);

    public async Task DeactivateAccountAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        await PostForSuccessAsync("api/MemberAccount/UpdateAccountStatusDeactive", new { PhoneNumber = phoneNumber }, cancellationToken);

    private async Task<IReadOnlyList<JsonElement>> GetArrayAsync(string endpoint, object? payload, CancellationToken cancellationToken)
    {
        using var response = payload is null
            ? await apiClient.GetAsync(endpoint, cancellationToken)
            : await apiClient.PostAsync(endpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(content) || content == "[]" || !content.StartsWith('['))
        {
            return Array.Empty<JsonElement>();
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private async Task<JsonElement?> GetObjectAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        using var response = await apiClient.PostAsync(endpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(content) || content is "[]" or "{}" || content[0] is not ('{' or '[')) return null;
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.GetArrayLength() == 0 ? null : document.RootElement[0].Clone();
        }
        return document.RootElement.Clone();
    }

    private async Task PostForSuccessAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        using var response = await apiClient.PostAsync(endpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static RewardCard MapReward(JsonElement item, bool owned, string fallbackCategory) => new(
        Int(item, "Id") ?? 0,
        Text(item, "Title", "Name", "ShortTitle") ?? fallbackCategory,
        Text(item, "Category", "Type") ?? fallbackCategory,
        Int(item, "Point") ?? 0,
        PlainText(item, "Description", "SubTitle") ?? string.Empty,
        owned,
        Text(item, "RewardId", "MemberRewardId", "VoucherId", "MemberVoucherId", "RedeemId", "Id"),
        Date(item, "ExpireDate"),
        Text(item, "DiscountAmount"));

    private static NotificationCard MapNotification(JsonElement item) => new(
        Text(item, "NotificationId", "Notification_Id", "Id") ?? string.Empty,
        Text(item, "Title") ?? "Notification",
        PlainText(item, "Content", "Description") ?? string.Empty,
        Text(item, "NotificationType", "Type") ?? "General",
        Date(item, "PushTime", "CreateDate") ?? DateTime.MinValue,
        Bool(item, "IsUnread") ?? false);

    private static ActivityRecord MapActivity(JsonElement item, string fallbackType)
    {
        var type = Text(item, "Type", "HistoryType") ?? fallbackType;
        var amount = Decimal(item, "Amount", "TopUpAmount", "SpendAmount", "TotalAmount");
        if ((type.Contains("spend", StringComparison.OrdinalIgnoreCase) ||
             type.Contains("payment", StringComparison.OrdinalIgnoreCase)) && amount is not null)
        {
            amount = -Math.Abs(amount.Value);
        }
        return new ActivityRecord(
            Date(item, "DateTime", "CreateDate", "TopupDate", "TransactionDate", "UseDate") ?? DateTime.MinValue,
            fallbackType,
            PlainText(item, "Description", "Name", "Title") ?? fallbackType,
            amount,
            Int(item, "Point", "RewardPoint"),
            Text(item, "TopupId", "TopUpId", "HistoryId", "ReferenceNumber", "Id"),
            Text(item, "Status"));
    }

    private static StampCard MapStamp(JsonElement item, bool used) => new(
        Text(item, "StampId", "StampTransactionRecordID", "AssignRecordId", "Id") ?? string.Empty,
        Text(item, "Name") ?? (used ? "Used stamps" : "Stamp card"),
        PlainText(item, "Description") ?? string.Empty,
        Int(item, used ? "TotalUseStamp" : "TotalAssignStamp", "Point", "FinalStamp") ?? 0,
        Date(item, used ? "UseDate" : "ReceiveDate", "ClaimDate", "ExpireDate", "CreateDate"),
        Text(item, "Status") ?? (used ? "Used" : "Active"),
        used);

    private static string? Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    private static string? PlainText(JsonElement element, params string[] names)
    {
        var value = Text(element, names);
        if (string.IsNullOrWhiteSpace(value)) return value;
        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static int? Int(JsonElement element, params string[] names)
    {
        var text = Text(element, names);
        return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static double? Double(JsonElement element, params string[] names)
    {
        var text = Text(element, names);
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static decimal? Decimal(JsonElement element, params string[] names)
    {
        var text = Text(element, names);
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTime? Date(JsonElement element, params string[] names)
    {
        var text = Text(element, names);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ? value : null;
    }

    private static bool? Bool(JsonElement element, params string[] names)
    {
        var text = Text(element, names);
        return bool.TryParse(text, out var value) ? value : null;
    }
}
