using System.Text.Json.Serialization;

namespace LoyaltyBackend.Models;

public sealed record RequestOtpRequest(
    [property: JsonPropertyName("PhoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("DeviceId")] string? DeviceId = null);

public sealed record OtpResponse(
    [property: JsonPropertyName("PhoneNumber")] string? PhoneNumber,
    [property: JsonPropertyName("OTP")] string? Otp,
    [property: JsonPropertyName("FirstLogin")] string? FirstLogin,
    [property: JsonPropertyName("DeviceId")] string? DeviceId,
    [property: JsonPropertyName("AccountStatus")] string? AccountStatus);

public sealed record RegisterMemberRequest(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Email")] string Email,
    [property: JsonPropertyName("PhoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("ReferralBy")] string? ReferralBy,
    [property: JsonPropertyName("Password")] string Password,
    [property: JsonPropertyName("Birthday")] string? Birthday,
    [property: JsonPropertyName("EmailSubcribe")] string? EmailSubscribe,
    [property: JsonPropertyName("Image")] string? Image = null,
    [property: JsonPropertyName("ImageByte")] string? ImageByte = null);

public sealed record PhoneLoginRequest(
    [property: JsonPropertyName("Phone")] string Phone,
    [property: JsonPropertyName("OTP")] string Otp,
    [property: JsonPropertyName("FirstLogin")] bool FirstLogin,
    [property: JsonPropertyName("DeviceId")] string? DeviceId,
    [property: JsonPropertyName("AccountStatus")] string? AccountStatus);

public sealed record EmailLoginRequest(
    [property: JsonPropertyName("Email")] string Email,
    [property: JsonPropertyName("Password")] string Password);

public sealed record PhoneNumberRequest(
    [property: JsonPropertyName("PhoneNumber")] string PhoneNumber);

public sealed record EmailRequest(
    [property: JsonPropertyName("Email")] string Email);

public sealed record ChangeDeviceRequest(
    [property: JsonPropertyName("PhoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("DeviceId")] string DeviceId);

public sealed record MemberDetailsResponse(
    string? UserId,
    string? Name,
    string? Email,
    string? PhoneNumber,
    string? BirthDate,
    string? ReferralCode,
    string? ReferralBy,
    string? Tier,
    string? Image,
    string? ImageByte,
    string? AccountStatus,
    string? WalletId,
    decimal Balance,
    decimal TotalTopupAmount,
    int? Point,
    DateTime? ExpireDate,
    double PointBonus,
    int TotalStamp,
    bool? EmailVerified,
    bool? PhoneVerified);

public sealed record WalletDetailsResponse(
    int Id,
    string? WalletId,
    string? UserId,
    string? PhoneNumber,
    decimal Balance,
    decimal TotalTopupAmount,
    int? Point,
    DateTime CreateDate,
    DateTime? ExpireDate,
    string? Status,
    double PointBonus,
    string? Tier,
    int TotalStamp);

public sealed class HistoryRecordResponse
{
    public int Id { get; init; }
    public string? HistoryId { get; init; }
    public string? Type { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Status { get; init; }

    [JsonPropertyName("DateTime")]
    public DateTime OccurredAt { get; init; }

    public string? ReferenceNumber { get; init; }
    public string? MerchantId { get; init; }
    public decimal Amount { get; init; }
    public int Stamp { get; init; }
    public string? Description { get; init; }
    public int Point { get; init; }
}
