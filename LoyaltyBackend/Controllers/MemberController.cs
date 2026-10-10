using LoyaltyBackend.Models;
using LoyaltyBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using QRCoder;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoyaltyBackend.Controllers;

[Authorize(Roles = "Member")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MemberController(
    ILoyaltyMemberService loyaltyMembers,
    ILoyaltyFeatureService features,
    SharedGatewayQrService qrService,
    IWebHostEnvironment environment) : Controller
{
    private const string EmailVerificationOtpKey = "Member.EmailVerificationOtp";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IActionResult> Dashboard()
    {
        var member = await GetMemberSummaryAsync();
        var activity = await GetActivityAsync();
        var phoneNumber = GetRequiredPhoneNumber();
        var featuredRewards = (await features.GetRewardsAsync(phoneNumber, HttpContext.RequestAborted)).Rewards;
        var highlights = await features.GetHighlightsAsync(HttpContext.RequestAborted);
        return View(new DashboardViewModel
        {
            Member = member,
            Highlights = highlights,
            FeaturedRewards = featuredRewards.Take(3).ToArray(),
            RecentActivity = activity.Take(4).ToArray()
        });
    }

    public async Task<IActionResult> Wallet()
    {
        var phoneNumber = GetRequiredPhoneNumber();
        var walletTask = loyaltyMembers.GetSummaryAsync(phoneNumber, HttpContext.RequestAborted);
        var rewardsTask = features.GetRewardsAsync(phoneNumber, HttpContext.RequestAborted);
        await Task.WhenAll(walletTask, rewardsTask);

        return View(new WalletPageViewModel
        {
            Wallet = walletTask.Result,
            ClaimedRewards = rewardsTask.Result.OwnedRewards,
            ClaimedVouchers = rewardsTask.Result.OwnedVouchers
        });
    }
    public async Task<IActionResult> Rewards()
    {
        var phoneNumber = GetRequiredPhoneNumber();
        return View(await features.GetRewardsAsync(phoneNumber, HttpContext.RequestAborted));
    }

    public async Task<IActionResult> RewardDetails(string id, bool voucher = false)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var reward = await features.GetRewardAsync(
            id, GetRequiredPhoneNumber(), voucher, HttpContext.RequestAborted);
        if (reward is null) return NotFound();
        ViewData["Voucher"] = voucher;
        var wallet = await loyaltyMembers.GetWalletAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        ViewData["CanRedeem"] = reward.CanRedeem && reward.Points <= wallet.Points;
        return View(reward);
    }

    public async Task<IActionResult> Stamps(int activeCount = 8, int usedCount = 8)
    {
        var stamps = await features.GetStampsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        activeCount = Math.Clamp(activeCount, 8, 10000);
        usedCount = Math.Clamp(usedCount, 8, 10000);
        ViewData["ActiveTotal"] = stamps.Active.Count;
        ViewData["UsedTotal"] = stamps.Used.Count;
        return View(new StampsViewModel
        {
            Active = stamps.Active.OrderByDescending(item => item.Date).ThenBy(item => item.Id).Take(activeCount).ToArray(),
            Used = stamps.Used.OrderByDescending(item => item.Date).ThenBy(item => item.Id).Take(usedCount).ToArray()
        });
    }

    public async Task<IActionResult> Notifications() =>
        View(await features.GetNotificationsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted));

    public async Task<IActionResult> NotificationDetails(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var memberNotifications = await features.GetNotificationsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        if (!memberNotifications.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal))) return NotFound();
        var notification = await features.GetNotificationAsync(id, GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        if (notification is null) return NotFound();
        return View(notification);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadNotification(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var notifications = await features.GetNotificationsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
            if (!notifications.Any(item => item.Id == id)) return NotFound();
            await features.MarkNotificationReadAsync(GetRequiredPhoneNumber(), id, HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Notification marked as read.";
        }
        return RedirectToAction(nameof(Notifications));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAllNotifications()
    {
        await features.MarkAllNotificationsReadAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        TempData["StatusMessage"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Notifications));
    }

    public async Task<IActionResult> Referrals()
    {
        var member = await loyaltyMembers.GetSummaryAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        return View(new ReferralsViewModel
        {
            ReferralCode = member.ReferralCode,
            Members = await features.GetReferralsAsync(member.ReferralCode, HttpContext.RequestAborted)
        });
    }
    public async Task<IActionResult> History(string? type = null, int count = 8)
    {
        var validTypes = new[] { "Wallet", "Top-up", "Points", "Stamp", "Stamps used", "Reward", "Voucher", "Spending", "Voucher activity" };
        if (!string.IsNullOrWhiteSpace(type) && !validTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest();
        }
        ViewData["Type"] = type;
        var records = string.IsNullOrWhiteSpace(type)
            ? await GetActivityAsync()
            : await features.GetHistoryAsync(GetRequiredPhoneNumber(), type, HttpContext.RequestAborted);
        ViewData["Total"] = records.Count;
        return View(records.Take(Math.Clamp(count, 8, 10000)).ToArray());
    }

    public async Task<IActionResult> TopUpDetails(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var memberTopUps = await features.GetHistoryAsync(GetRequiredPhoneNumber(), "Top-up", HttpContext.RequestAborted);
        if (!memberTopUps.Any(item => string.Equals(item.ReferenceId, id, StringComparison.Ordinal))) return NotFound();
        var activity = await features.GetTopUpDetailsAsync(id, HttpContext.RequestAborted);
        return activity is null ? NotFound() : View("ActivityDetails", activity);
    }

    public async Task<IActionResult> Outlets() => View(await features.GetOutletsAsync(HttpContext.RequestAborted));
    public async Task<IActionResult> Profile() => View(await GetMemberSummaryAsync());

    public async Task<IActionResult> BalanceSnapshot()
    {
        var member = await GetMemberSummaryAsync();
        return Json(new { balance = member.WalletBalance, points = member.Points, stamps = member.Stamps, tier = member.Tier });
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> MemberQr()
    {
        var payload = await qrService.CreateAsync(GetRequiredPhoneNumber(), cancellationToken: HttpContext.RequestAborted);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return Content(qr.GetGraphic(6), "image/svg+xml");
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RewardQr(string id, bool voucher = false)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var memberItems = await features.GetRewardsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        var ownedItem = memberItems.OwnedRewards.Concat(memberItems.OwnedVouchers)
            .FirstOrDefault(item => string.Equals(item.RewardId, id, StringComparison.Ordinal));
        var isVoucher = voucher || memberItems.OwnedVouchers.Any(item => item.RewardId == id);
        var item = ownedItem ?? await features.GetRewardAsync(id, GetRequiredPhoneNumber(), isVoucher, HttpContext.RequestAborted);
        if (item is null) return NotFound();
        if (!item.CanRedeem) return BadRequest("This item is expired or unavailable.");
        var payload = await qrService.CreateAsync(GetRequiredPhoneNumber(), id, isVoucher, HttpContext.RequestAborted);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return Content(qr.GetGraphic(6), "image/svg+xml");
    }

    public async Task<IActionResult> EditProfile()
    {
        var member = await loyaltyMembers.GetDetailsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        return View(new ProfileEditViewModel
        {
            Name = member.Name ?? string.Empty,
            Email = member.Email ?? string.Empty,
            Birthday = DateTime.TryParse(member.BirthDate, out var birthday) ? birthday : null
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile(ProfileEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (model.Photo is not null)
        {
            if (model.Photo.Length > 2 * 1024 * 1024 || !model.Photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Photo), "Choose an image smaller than 2 MB.");
                return View(model);
            }
            await using var photoStream = new MemoryStream();
            await model.Photo.CopyToAsync(photoStream, HttpContext.RequestAborted);
            model.ImageByte = Convert.ToBase64String(photoStream.ToArray());
        }
        if (model.Photo is null)
        {
            var current = await loyaltyMembers.GetDetailsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
            model.ImageByte = current.ImageByte ?? string.Empty;
        }
        await features.UpdateProfileAsync(GetRequiredPhoneNumber(), model, HttpContext.RequestAborted);
        TempData["StatusMessage"] = "Your profile was updated.";
        return RedirectToAction(nameof(Profile));
    }

    public async Task<IActionResult> Feedback() => View(new FeedbackPageViewModel
    {
        History = await features.GetFeedbackAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted)
    });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Feedback(FeedbackPageViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.History = await features.GetFeedbackAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            return View(model);
        }
        await features.SubmitFeedbackAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, model.Form, HttpContext.RequestAborted);
        TempData["StatusMessage"] = "Thank you. Your feedback was submitted.";
        return RedirectToAction(nameof(Profile));
    }

    public IActionResult VerifyEmail()
    {
        ViewData["VerificationRequested"] = HttpContext.Session.GetString(EmailVerificationOtpKey) is not null;
        if (environment.IsDevelopment()) ViewData["OtpHint"] = HttpContext.Session.GetString(EmailVerificationOtpKey);
        return View(new OtpChallengeViewModel { PhoneNumber = GetRequiredPhoneNumber() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestEmailVerification(OtpChallengeViewModel model)
    {
        model.PhoneNumber = GetRequiredPhoneNumber();
        using var response = await HttpContext.RequestServices.GetRequiredService<ILoyaltyApiClient>().PostAsync(
            "api/MemberAccount/GenerateMailOTP", new RequestOtpRequest(model.PhoneNumber), HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = "The verification email could not be sent.";
            return RedirectToAction(nameof(Profile));
        }
        var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        var otp = ReadOtp(body);
        if (string.IsNullOrWhiteSpace(otp))
        {
            TempData["ErrorMessage"] = "The email verification service returned an unsupported response.";
            return RedirectToAction(nameof(Profile));
        }
        VerificationChallenge.Store(HttpContext.Session, EmailVerificationOtpKey, otp);
        // Requesting a code does not require the code-entry field yet.
        ModelState.Clear();
        ViewData["VerificationRequested"] = true;
        if (environment.IsDevelopment()) ViewData["OtpHint"] = otp;
        return View("VerifyEmail", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(OtpChallengeViewModel model)
    {
        model.PhoneNumber = GetRequiredPhoneNumber();
        var expected = HttpContext.Session.GetString(EmailVerificationOtpKey);
        if (!ModelState.IsValid || !VerificationChallenge.Matches(HttpContext.Session, EmailVerificationOtpKey, model.Otp))
        {
            ViewData["VerificationRequested"] = true;
            if (environment.IsDevelopment()) ViewData["OtpHint"] = expected;
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }
        using var response = await HttpContext.RequestServices.GetRequiredService<ILoyaltyApiClient>().PostAsync(
            "api/MemberAccount/UpdateAccountVerify", new { PhoneNumber = model.PhoneNumber, Type = "Email" }, HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();
        VerificationChallenge.Clear(HttpContext.Session, EmailVerificationOtpKey);
        TempData["StatusMessage"] = "Your email was verified.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(bool confirm)
    {
        if (!confirm)
        {
            TempData["ErrorMessage"] = "Confirm account deactivation before continuing.";
            return RedirectToAction(nameof(Profile));
        }

        await features.DeactivateAccountAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusMessage"] = "Your account was deactivated.";
        return RedirectToAction("Index", "Home");
    }

    private async Task<MemberSummary> GetMemberSummaryAsync()
    {
        return await loyaltyMembers.GetSummaryAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
    }

    private async Task<IReadOnlyList<ActivityRecord>> GetActivityAsync()
    {
        return await features.GetHistoryAsync(GetRequiredPhoneNumber(), "all", HttpContext.RequestAborted);
    }

    private string GetRequiredPhoneNumber() =>
        User.FindFirstValue(ClaimTypes.MobilePhone)
        ?? throw new InvalidOperationException("The signed-in member does not have a phone-number claim.");

    private static string ReadOtp(string body)
    {
        try
        {
            var result = JsonSerializer.Deserialize<OtpResponse>(body, JsonOptions);
            if (result?.Otp is { Length: > 0 } && Regex.IsMatch(result.Otp, @"^\d{4,10}$")) return result.Otp;
        }
        catch (JsonException)
        {
            // The provider may return a plain OTP rather than an object.
        }

        var candidate = body.Trim().Trim('"');
        return Regex.IsMatch(candidate, @"^\d{4,10}$") ? candidate : string.Empty;
    }
}
