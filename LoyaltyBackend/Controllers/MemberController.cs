using LoyaltyBackend.Models;
using LoyaltyBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using QRCoder;
using System.Text.RegularExpressions;

namespace LoyaltyBackend.Controllers;

[Authorize(Roles = "Member")]
public sealed class MemberController(
    ILoyaltyMemberService loyaltyMembers,
    ILoyaltyFeatureService features) : Controller
{
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
        return View(await loyaltyMembers.GetWalletAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted));
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
        return View(reward);
    }

    public async Task<IActionResult> Stamps() =>
        View(await features.GetStampsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted));

    public async Task<IActionResult> Notifications() =>
        View(await features.GetNotificationsAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted));

    public async Task<IActionResult> NotificationDetails(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var notification = await features.GetNotificationAsync(id, HttpContext.RequestAborted);
        if (notification is null) return NotFound();
        return View(notification);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadNotification(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            await features.MarkNotificationReadAsync(GetRequiredPhoneNumber(), id, HttpContext.RequestAborted);
        }
        return RedirectToAction(nameof(Notifications));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAllNotifications()
    {
        await features.MarkAllNotificationsReadAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
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
    public async Task<IActionResult> History(string? type = null)
    {
        var validTypes = new[] { "Wallet", "Top-up", "Points", "Stamp", "Reward", "Voucher" };
        if (!string.IsNullOrWhiteSpace(type) && !validTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest();
        }
        ViewData["Type"] = type;
        return View(string.IsNullOrWhiteSpace(type)
            ? await GetActivityAsync()
            : await features.GetHistoryAsync(GetRequiredPhoneNumber(), type, HttpContext.RequestAborted));
    }

    public async Task<IActionResult> TopUpDetails(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var activity = await features.GetTopUpDetailsAsync(id, HttpContext.RequestAborted);
        return activity is null ? NotFound() : View("ActivityDetails", activity);
    }

    public async Task<IActionResult> Outlets() => View(await features.GetOutletsAsync(HttpContext.RequestAborted));
    public async Task<IActionResult> Profile() => View(await GetMemberSummaryAsync());

    public IActionResult MemberQr()
    {
        var payload = GetRequiredPhoneNumber();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return Content(qr.GetGraphic(6), "image/svg+xml");
    }

    public IActionResult RewardQr(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(id, QRCodeGenerator.ECCLevel.Q);
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

    public IActionResult VerifyEmail() => View(new OtpChallengeViewModel { PhoneNumber = GetRequiredPhoneNumber() });

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
        var digits = Regex.Replace(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted), "[^0-9]", string.Empty);
        if (digits.Length is < 4 or > 10)
        {
            TempData["ErrorMessage"] = "The email verification service returned an unsupported response.";
            return RedirectToAction(nameof(Profile));
        }
        TempData["EmailVerificationOtp"] = digits;
        return View("VerifyEmail", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(OtpChallengeViewModel model)
    {
        model.PhoneNumber = GetRequiredPhoneNumber();
        var expected = TempData.Peek("EmailVerificationOtp") as string;
        if (!ModelState.IsValid || !string.Equals(Regex.Replace(model.Otp ?? string.Empty, "[^0-9]", string.Empty), expected, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }
        using var response = await HttpContext.RequestServices.GetRequiredService<ILoyaltyApiClient>().PostAsync(
            "api/MemberAccount/UpdateAccountVerify", new { PhoneNumber = model.PhoneNumber, Type = "Email" }, HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();
        TempData.Remove("EmailVerificationOtp");
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
        return await loyaltyMembers.GetActivityAsync(GetRequiredPhoneNumber(), HttpContext.RequestAborted);
    }

    private string GetRequiredPhoneNumber() =>
        User.FindFirstValue(ClaimTypes.MobilePhone)
        ?? throw new InvalidOperationException("The signed-in member does not have a phone-number claim.");
}
