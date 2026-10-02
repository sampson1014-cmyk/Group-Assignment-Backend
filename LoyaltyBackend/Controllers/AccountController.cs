using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoyaltyBackend.Models;
using LoyaltyBackend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LoyaltyBackend.Controllers;

[EnableRateLimiting("account")]
public sealed class AccountController(ILoyaltyApiClient loyaltyApiClient) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using var response = await loyaltyApiClient.PostAsync(
            "api/MemberLogin/CheckEmailPassword",
            new EmailLoginRequest(model.Identifier, model.Password),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "The email or password is incorrect.");
            return View(model);
        }

        var member = await response.Content.ReadFromJsonAsync<MemberDetailsResponse>(cancellationToken: HttpContext.RequestAborted);
        if (member is null)
        {
            ModelState.AddModelError(string.Empty, "The member profile could not be loaded.");
            return View(model);
        }

        if (!string.Equals(member.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "This member account is not active. Please contact support.");
            return View(model);
        }

        await SignInMemberAsync(
            member.Name ?? model.Identifier,
            member.UserId ?? model.Identifier,
            member.PhoneNumber,
            model.RememberMe);

        if (Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Dashboard", "Member");
    }

    [AllowAnonymous]
    public IActionResult PhoneLogin() => View(new PhoneLoginViewModel { PhoneNumber = "+60" });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> PhoneLogin(PhoneLoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using var response = await loyaltyApiClient.PostAsync(
            "api/MemberAccount/RequestOTP",
            new RequestOtpRequest(model.PhoneNumber),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send a verification code. Check the number and try again.");
            return View(model);
        }

        TempData["OtpPhoneNumber"] = model.PhoneNumber;
        return RedirectToAction(nameof(VerifyOtp));
    }

    [AllowAnonymous]
    public IActionResult VerifyOtp()
    {
        var phoneNumber = TempData.Peek("OtpPhoneNumber") as string;
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return RedirectToAction(nameof(PhoneLogin));
        }

        return View(new VerifyOtpViewModel { PhoneNumber = phoneNumber });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        using var response = await loyaltyApiClient.PostAsync(
            "api/MemberLogin/MemberMobileLoginGetProfile",
            new PhoneLoginRequest(model.PhoneNumber, model.Otp, false, null, "Active"),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid or expired.");
            return View(model);
        }

        MemberDetailsResponse? member;
        try
        {
            member = await response.Content.ReadFromJsonAsync<MemberDetailsResponse>(cancellationToken: HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            member = null;
        }

        if (member is null)
        {
            ModelState.AddModelError(nameof(model.Otp), "The member profile could not be loaded.");
            return View(model);
        }

        if (!string.Equals(member.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "This member account is not active. Please contact support.");
            return View(model);
        }

        await SignInMemberAsync(
            member.Name ?? model.PhoneNumber,
            member.UserId ?? model.PhoneNumber,
            member.PhoneNumber ?? model.PhoneNumber,
            rememberMe: false);
        TempData.Remove("OtpPhoneNumber");
        return RedirectToAction("Dashboard", "Member");
    }

    [AllowAnonymous]
    public IActionResult Register() => View();

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.ReferralCode))
        {
            using var referralResponse = await loyaltyApiClient.PostAsync(
                "api/MemberWallet/CheckReffererCodeValid",
                new { ReferralBy = model.ReferralCode },
                HttpContext.RequestAborted);
            var referralResult = await referralResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!referralResponse.IsSuccessStatusCode || referralResult.Contains("false", StringComparison.OrdinalIgnoreCase) || referralResult.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.ReferralCode), "This referral code is not valid.");
                return View(model);
            }
        }

        using var otpResponse = await loyaltyApiClient.PostAsync(
            "api/MemberAccount/RegisterOtp",
            new RequestOtpRequest(model.PhoneNumber),
            HttpContext.RequestAborted);
        if (!otpResponse.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send the registration code.");
            return View(model);
        }

        var otpBody = await otpResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        var normalizedOtp = NormalizeOtp(otpBody);
        if (string.IsNullOrWhiteSpace(normalizedOtp))
        {
            ModelState.AddModelError(string.Empty, "The registration service did not return a usable verification challenge.");
            return View(model);
        }

        TempData["PendingRegistration"] = JsonSerializer.Serialize(model);
        TempData["PendingRegistrationOtp"] = normalizedOtp;
        return RedirectToAction(nameof(VerifyRegistration));
    }

    [AllowAnonymous]
    public IActionResult VerifyRegistration()
    {
        var json = TempData.Peek("PendingRegistration") as string;
        var registration = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RegisterViewModel>(json);
        return registration is null
            ? RedirectToAction(nameof(Register))
            : View(new OtpChallengeViewModel { PhoneNumber = registration.PhoneNumber });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyRegistration(OtpChallengeViewModel model)
    {
        var json = TempData.Peek("PendingRegistration") as string;
        var expectedOtp = TempData.Peek("PendingRegistrationOtp") as string;
        var registration = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RegisterViewModel>(json);
        if (registration is null || string.IsNullOrWhiteSpace(expectedOtp)) return RedirectToAction(nameof(Register));
        if (!ModelState.IsValid) return View(model);
        if (!string.Equals(NormalizeOtp(model.Otp), expectedOtp, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }

        using var response = await loyaltyApiClient.PostAsync("api/MemberLogin/RegisterMember", new RegisterMemberRequest(
            registration.FullName,
            registration.Email,
            registration.PhoneNumber,
            registration.ReferralCode,
            registration.Password,
            registration.Birthday?.ToString("yyyy-MM-dd"),
            registration.EmailSubscribe.ToString().ToLowerInvariant()), HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "Registration could not be completed.");
            return View(model);
        }

        TempData.Remove("PendingRegistration");
        TempData.Remove("PendingRegistrationOtp");
        TempData["StatusMessage"] = "Your member account was created. You can now log in.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel { PhoneNumber = "+60" });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await loyaltyApiClient.PostAsync("api/MemberAccount/RequestOTP", new RequestOtpRequest(model.PhoneNumber), HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send a reset code.");
            return View(model);
        }
        var otp = NormalizeOtp(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
        if (string.IsNullOrWhiteSpace(otp))
        {
            ModelState.AddModelError(string.Empty, "The password service did not return a usable verification challenge.");
            return View(model);
        }
        TempData["ResetPhone"] = model.PhoneNumber;
        TempData["ResetOtp"] = otp;
        return RedirectToAction(nameof(ResetPassword));
    }

    [AllowAnonymous]
    public IActionResult ResetPassword()
    {
        var phone = TempData.Peek("ResetPhone") as string;
        return string.IsNullOrWhiteSpace(phone)
            ? RedirectToAction(nameof(ForgotPassword))
            : View(new ResetPasswordViewModel { PhoneNumber = phone });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var expectedOtp = TempData.Peek("ResetOtp") as string;
        if (string.IsNullOrWhiteSpace(expectedOtp)) return RedirectToAction(nameof(ForgotPassword));
        if (!ModelState.IsValid) return View(model);
        if (!string.Equals(NormalizeOtp(model.Otp), expectedOtp, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }
        using var response = await loyaltyApiClient.PostAsync("api/MemberAccount/MemberResetPassword", new { model.PhoneNumber, model.NewPassword }, HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "The password could not be reset.");
            return View(model);
        }
        TempData.Remove("ResetPhone");
        TempData.Remove("ResetOtp");
        TempData["StatusMessage"] = "Your password was reset. Log in with the new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private async Task SignInMemberAsync(string displayName, string identifier, string? phoneNumber, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identifier),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, "Member")
        };

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, phoneNumber));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = rememberMe });
    }

    private static string NormalizeOtp(string value)
    {
        var digits = Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);
        return digits.Length is >= 4 and <= 10 ? digits : string.Empty;
    }
}
