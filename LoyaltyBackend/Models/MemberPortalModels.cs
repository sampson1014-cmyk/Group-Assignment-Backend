using System.ComponentModel.DataAnnotations;

namespace LoyaltyBackend.Models;

public sealed class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email address")]
    public string Identifier { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class PhoneLoginViewModel
{
    [Required, Phone, Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class VerifyOtpViewModel
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(10, MinimumLength = 4), Display(Name = "Verification code")]
    public string Otp { get; set; } = string.Empty;
}

public sealed class RegisterViewModel
{
    [Required, Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, Phone, Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Referral code")]
    public string? ReferralCode { get; set; }

    [DataType(DataType.Date)]
    public DateTime? Birthday { get; set; }

    [Display(Name = "Email me member offers")]
    public bool EmailSubscribe { get; set; }
}

public class OtpChallengeViewModel
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(10, MinimumLength = 4), Display(Name = "Verification code")]
    public string Otp { get; set; } = string.Empty;
}

public sealed class ForgotPasswordViewModel
{
    [Required, Phone, Display(Name = "Registered phone number")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel : OtpChallengeViewModel
{
    [Required, DataType(DataType.Password), MinLength(6), Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record MemberSummary(
    string Name,
    string MemberCode,
    string Tier,
    decimal WalletBalance,
    int Points,
    int Stamps,
    string ReferralCode,
    DateTime? PointsExpireAt,
    string? PhotoSource = null);

public sealed record RewardCard(
    int Id,
    string Name,
    string Category,
    int Points,
    string Description,
    bool Owned = false,
    string? RewardId = null,
    DateTime? ExpireAt = null,
    string? DiscountAmount = null);

public sealed record ActivityRecord(
    DateTime OccurredAt,
    string Type,
    string Description,
    decimal? Amount,
    int? Points,
    string? ReferenceId = null,
    string? Status = null);

public sealed record OutletCard(int Id, string Name, string Address, string Phone, double Latitude, double Longitude);

public sealed record StampCard(string Id, string Name, string Description, int Stamps, DateTime? Date, string Status, bool Used);

public sealed record NotificationCard(string Id, string Title, string Content, string Type, DateTime SentAt, bool IsUnread);

public sealed record ReferralMemberCard(string Name, string Tier, DateTime? JoinedAt);
public sealed record HighlightCard(string Title, string Subtitle, string Description, string? Link, string? ButtonText, DateTime? ExpireAt);

public sealed class RewardsViewModel
{
    public required IReadOnlyList<RewardCard> Rewards { get; init; }
    public required IReadOnlyList<RewardCard> Vouchers { get; init; }
    public required IReadOnlyList<RewardCard> OwnedRewards { get; init; }
    public required IReadOnlyList<RewardCard> OwnedVouchers { get; init; }
}

public sealed class StampsViewModel
{
    public required IReadOnlyList<StampCard> Active { get; init; }
    public required IReadOnlyList<StampCard> Used { get; init; }
}

public sealed class ReferralsViewModel
{
    public required string ReferralCode { get; init; }
    public required IReadOnlyList<ReferralMemberCard> Members { get; init; }
}

public sealed class ProfileEditViewModel
{
    [Required, Display(Name = "Full name")]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [DataType(DataType.Date), Display(Name = "Birthday")]
    public DateTime? Birthday { get; set; }

    [Display(Name = "Profile photo")]
    public IFormFile? Photo { get; set; }

    public string? ImageByte { get; set; }
}

public sealed class FeedbackViewModel
{
    [Required, StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required, Range(1, 5)]
    public int Rating { get; set; } = 5;

    [Required]
    public string Category { get; set; } = "General";

    public string? Location { get; set; }
}

public sealed record FeedbackRecordCard(string Title, string Description, int Rating, string Category, DateTime? CreatedAt);

public sealed class FeedbackPageViewModel
{
    public FeedbackViewModel Form { get; set; } = new();
    public IReadOnlyList<FeedbackRecordCard> History { get; set; } = Array.Empty<FeedbackRecordCard>();
}

public sealed class DashboardViewModel
{
    public required MemberSummary Member { get; init; }
    public required IReadOnlyList<HighlightCard> Highlights { get; init; }
    public required IReadOnlyList<RewardCard> FeaturedRewards { get; init; }
    public required IReadOnlyList<ActivityRecord> RecentActivity { get; init; }
}
