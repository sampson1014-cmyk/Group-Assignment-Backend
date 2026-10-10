using System.Security.Claims;
using System.Net;
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
public sealed class AccountController(
    ILoyaltyApiClient loyaltyApiClient,
    IWebHostEnvironment environment) : Controller
{
    private const string PhoneLoginChallengeKey = "Account.PhoneLoginChallenge";
    private const string PhoneLoginOtpKey = "Account.PhoneLoginOtp";
    private const string PendingRegistrationKey = "Account.PendingRegistration";
    private const string PendingRegistrationOtpKey = "Account.PendingRegistrationOtp";
    private const string ResetPhoneKey = "Account.ResetPhone";
    private const string ResetOtpKey = "Account.ResetOtp";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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
            if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)) response.EnsureSuccessStatusCode();
            ModelState.AddModelError(string.Empty, "The email or password is incorrect.");
            return View(model);
        }

        var member = await ReadMemberResponseAsync(response, HttpContext.RequestAborted);
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

        var deviceId = GetOrCreateDeviceId();
        // Web login must not replace the phone's push registration with a browser ID.
        using (var profileResponse = await loyaltyApiClient.PostAsync("api/MemberDetails/GetMemberDetails", new PhoneNumberRequest(model.PhoneNumber), HttpContext.RequestAborted))
        {
            profileResponse.EnsureSuccessStatusCode();
            using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            var record = profile.RootElement.ValueKind == JsonValueKind.Array && profile.RootElement.GetArrayLength() > 0 ? profile.RootElement[0] : profile.RootElement;
            if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty("DeviceId", out var registered) && !string.IsNullOrWhiteSpace(registered.GetString()))
                deviceId = registered.GetString()!;
        }
        using var response = await loyaltyApiClient.PostAsync(
            "api/MemberAccount/RequestOTP",
            new RequestOtpRequest(model.PhoneNumber, deviceId),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send a verification code. Check the number and try again.");
            return View(model);
        }

        var challenge = await ReadOtpResponseAsync(response, model.PhoneNumber, deviceId, HttpContext.RequestAborted);
        if (challenge is null || string.IsNullOrWhiteSpace(NormalizeOtp(challenge.Otp)))
        {
            ModelState.AddModelError(string.Empty, "The verification service returned an unsupported response.");
            return View(model);
        }

        HttpContext.Session.SetString(PhoneLoginChallengeKey, JsonSerializer.Serialize(challenge));
        VerificationChallenge.Store(HttpContext.Session, PhoneLoginOtpKey, NormalizeOtp(challenge.Otp));
        return RedirectToAction(nameof(VerifyOtp));
    }

    [AllowAnonymous]
    public IActionResult VerifyOtp()
    {
        var challenge = GetSessionJson<OtpResponse>(PhoneLoginChallengeKey);
        if (challenge is null || string.IsNullOrWhiteSpace(challenge.PhoneNumber))
        {
            return RedirectToAction(nameof(PhoneLogin));
        }

        if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
        return View(new VerifyOtpViewModel { PhoneNumber = challenge.PhoneNumber });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
    {
        var challenge = GetSessionJson<OtpResponse>(PhoneLoginChallengeKey);
        if (challenge is null || string.IsNullOrWhiteSpace(challenge.PhoneNumber))
        {
            return RedirectToAction(nameof(PhoneLogin));
        }
        model.PhoneNumber = challenge.PhoneNumber;
        if (!ModelState.IsValid)
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
            return View(model);
        }

        if (!VerificationChallenge.Matches(HttpContext.Session, PhoneLoginOtpKey, model.Otp))
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid or expired. Request another code if needed.");
            return View(model);
        }
        var firstLogin = bool.TryParse(challenge.FirstLogin, out var parsedFirstLogin) && parsedFirstLogin;
        var deviceId = string.IsNullOrWhiteSpace(challenge.DeviceId) ? GetOrCreateDeviceId() : challenge.DeviceId;

        using var response = await loyaltyApiClient.PostAsync(
            "api/MemberLogin/MemberMobileLoginGetProfile",
            new PhoneLoginRequest(model.PhoneNumber, model.Otp, firstLogin, deviceId, challenge.AccountStatus),
            HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid or expired.");
            return View(model);
        }

        var member = await ReadMemberResponseAsync(response, HttpContext.RequestAborted);

        if (member is null)
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
            ModelState.AddModelError(nameof(model.Otp), "The member profile could not be loaded.");
            return View(model);
        }

        if (!string.Equals(member.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = NormalizeOtp(challenge.Otp);
            ModelState.AddModelError(string.Empty, "This member account is not active. Please contact support.");
            return View(model);
        }

        await SignInMemberAsync(
            member.Name ?? model.PhoneNumber,
            member.UserId ?? model.PhoneNumber,
            member.PhoneNumber ?? model.PhoneNumber,
            rememberMe: false);
        HttpContext.Session.Remove(PhoneLoginChallengeKey);
        VerificationChallenge.Clear(HttpContext.Session, PhoneLoginOtpKey);
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

        using (var phoneResponse = await loyaltyApiClient.PostAsync(
                   "api/ManageMember/FindMemberByPhone",
                   new PhoneNumberRequest(model.PhoneNumber),
                   HttpContext.RequestAborted))
        using (var emailResponse = await loyaltyApiClient.PostAsync(
                   "api/ManageMember/FindMemberByEmail",
                   new EmailRequest(model.Email),
                   HttpContext.RequestAborted))
        {
            if (await HasMemberRecordAsync(phoneResponse, HttpContext.RequestAborted))
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), "This phone number is already registered.");
            }
            if (await HasMemberRecordAsync(emailResponse, HttpContext.RequestAborted))
            {
                ModelState.AddModelError(nameof(model.Email), "This email address is already registered.");
            }
            if (!ModelState.IsValid) return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.ReferralCode))
        {
            using var referralResponse = await loyaltyApiClient.PostAsync(
                "api/MemberWallet/CheckReffererCodeValid",
                new { ReferralBy = model.ReferralCode },
                HttpContext.RequestAborted);
            var referralResult = await referralResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!referralResponse.IsSuccessStatusCode || !IsValidReferralResult(referralResult))
            {
                ModelState.AddModelError(nameof(model.ReferralCode), "This referral code is not valid.");
                return View(model);
            }
        }

        var deviceId = GetOrCreateDeviceId();
        using var otpResponse = await loyaltyApiClient.PostAsync(
            "api/MemberAccount/RegisterOtp",
            new RequestOtpRequest(model.PhoneNumber, deviceId),
            HttpContext.RequestAborted);
        if (!otpResponse.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send the registration code.");
            return View(model);
        }

        var otpResult = await ReadOtpResponseAsync(otpResponse, model.PhoneNumber, deviceId, HttpContext.RequestAborted);
        var normalizedOtp = NormalizeOtp(otpResult?.Otp);
        if (string.IsNullOrWhiteSpace(normalizedOtp))
        {
            ModelState.AddModelError(string.Empty, "The registration service did not return a usable verification challenge.");
            return View(model);
        }

        HttpContext.Session.SetString(PendingRegistrationKey, JsonSerializer.Serialize(model));
        VerificationChallenge.Store(HttpContext.Session, PendingRegistrationOtpKey, normalizedOtp);
        return RedirectToAction(nameof(VerifyRegistration));
    }

    [AllowAnonymous]
    public IActionResult VerifyRegistration()
    {
        var json = HttpContext.Session.GetString(PendingRegistrationKey);
        var registration = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RegisterViewModel>(json);
        if (environment.IsDevelopment()) ViewData["OtpHint"] = HttpContext.Session.GetString(PendingRegistrationOtpKey);
        return registration is null
            ? RedirectToAction(nameof(Register))
            : View(new OtpChallengeViewModel { PhoneNumber = registration.PhoneNumber });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyRegistration(OtpChallengeViewModel model)
    {
        var json = HttpContext.Session.GetString(PendingRegistrationKey);
        var expectedOtp = HttpContext.Session.GetString(PendingRegistrationOtpKey);
        var registration = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RegisterViewModel>(json);
        if (registration is null || string.IsNullOrWhiteSpace(expectedOtp)) return RedirectToAction(nameof(Register));
        if (!ModelState.IsValid) return View(model);
        if (!VerificationChallenge.Matches(HttpContext.Session, PendingRegistrationOtpKey, model.Otp))
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = expectedOtp;
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }

        using var response = await loyaltyApiClient.PostAsync("api/MemberLogin/RegisterMember", new RegisterMemberRequest(
            registration.FullName,
            registration.Email,
            registration.PhoneNumber,
            registration.ReferralCode ?? string.Empty,
            registration.Password,
            registration.Birthday?.ToString("yyyy-MM-dd") ?? string.Empty,
            registration.EmailSubscribe.ToString().ToLowerInvariant()), HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "Registration could not be completed.");
            return View(model);
        }

        HttpContext.Session.Remove(PendingRegistrationKey);
        VerificationChallenge.Clear(HttpContext.Session, PendingRegistrationOtpKey);
        TempData["StatusMessage"] = "Your member account was created. You can now log in.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel { PhoneNumber = "+60" });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var deviceId = GetOrCreateDeviceId();
        using var response = await loyaltyApiClient.PostAsync("api/MemberAccount/RequestOTP", new RequestOtpRequest(model.PhoneNumber, deviceId), HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "We could not send a reset code.");
            return View(model);
        }
        var otpResult = await ReadOtpResponseAsync(response, model.PhoneNumber, deviceId, HttpContext.RequestAborted);
        var otp = NormalizeOtp(otpResult?.Otp);
        if (string.IsNullOrWhiteSpace(otp))
        {
            ModelState.AddModelError(string.Empty, "The password service did not return a usable verification challenge.");
            return View(model);
        }
        HttpContext.Session.SetString(ResetPhoneKey, model.PhoneNumber);
        VerificationChallenge.Store(HttpContext.Session, ResetOtpKey, otp);
        return RedirectToAction(nameof(ResetPassword));
    }

    [AllowAnonymous]
    public IActionResult ResetPassword()
    {
        var phone = HttpContext.Session.GetString(ResetPhoneKey);
        if (environment.IsDevelopment()) ViewData["OtpHint"] = HttpContext.Session.GetString(ResetOtpKey);
        return string.IsNullOrWhiteSpace(phone)
            ? RedirectToAction(nameof(ForgotPassword))
            : View(new ResetPasswordViewModel { PhoneNumber = phone });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var expectedOtp = HttpContext.Session.GetString(ResetOtpKey);
        if (string.IsNullOrWhiteSpace(expectedOtp)) return RedirectToAction(nameof(ForgotPassword));
        model.PhoneNumber = HttpContext.Session.GetString(ResetPhoneKey) ?? model.PhoneNumber;
        if (!ModelState.IsValid) return View(model);
        if (!VerificationChallenge.Matches(HttpContext.Session, ResetOtpKey, model.Otp))
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = expectedOtp;
            ModelState.AddModelError(nameof(model.Otp), "The verification code is invalid.");
            return View(model);
        }
        using var response = await loyaltyApiClient.PostAsync("api/MemberAccount/MemberResetPassword", new { model.PhoneNumber, model.NewPassword }, HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            if (environment.IsDevelopment()) ViewData["OtpHint"] = expectedOtp;
            ModelState.AddModelError(string.Empty, "The password could not be reset.");
            return View(model);
        }
        HttpContext.Session.Remove(ResetPhoneKey);
        VerificationChallenge.Clear(HttpContext.Session, ResetOtpKey);
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

    private string GetOrCreateDeviceId()
    {
        const string cookieName = "Eduvo.DeviceId";
        if (Request.Cookies.TryGetValue(cookieName, out var existing) && Guid.TryParse(existing, out _))
        {
            return existing;
        }

        var deviceId = Guid.NewGuid().ToString();
        Response.Cookies.Append(cookieName, deviceId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365)
        });
        return deviceId;
    }

    private T? GetSessionJson<T>(string key)
    {
        var json = HttpContext.Session.GetString(key);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task<OtpResponse?> ReadOtpResponseAsync(
        HttpResponseMessage response,
        string fallbackPhone,
        string fallbackDeviceId,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var result = JsonSerializer.Deserialize<OtpResponse>(body, JsonOptions);
            if (result is not null)
            {
                return result with
                {
                    PhoneNumber = string.IsNullOrWhiteSpace(result.PhoneNumber) ? fallbackPhone : result.PhoneNumber,
                    DeviceId = string.IsNullOrWhiteSpace(result.DeviceId) ? fallbackDeviceId : result.DeviceId
                };
            }
        }
        catch (JsonException)
        {
            // Some provider operations return the OTP as a plain string instead of JSON.
        }

        var plainOtp = NormalizeOtp(body);
        return string.IsNullOrWhiteSpace(plainOtp)
            ? null
            : new OtpResponse(fallbackPhone, plainOtp, null, fallbackDeviceId, null);
    }

    private static async Task<MemberDetailsResponse?> ReadMemberResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(body) || body is "null" or "{}" or "[]") return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var memberJson = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength() == 0 ? null : document.RootElement[0].GetRawText()
                : document.RootElement.GetRawText();
            return memberJson is null
                ? null
                : JsonSerializer.Deserialize<MemberDetailsResponse>(memberJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> HasMemberRecordAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (Regex.IsMatch(body, @"member\s+not\s+found", RegexOptions.IgnoreCase)) return false;
        response.EnsureSuccessStatusCode();
        if (string.IsNullOrWhiteSpace(body) || body is "null" or "{}" or "[]") return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            var record = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength() == 0 ? default : document.RootElement[0]
                : document.RootElement;
            if (record.ValueKind != JsonValueKind.Object) return false;
            return HasValue(record, "PhoneNumber") || HasValue(record, "Email") || HasValue(record, "UserId") ||
                   (record.TryGetProperty("Id", out var id) && id.TryGetInt32(out var numericId) && numericId > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasValue(JsonElement record, string propertyName) =>
        record.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool IsValidReferralResult(string body)
    {
        var result = body.Trim().Trim('"');
        return result.Equals("Referral Code Exist", StringComparison.OrdinalIgnoreCase)
            || result.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOtp(string? value)
    {
        var candidate = (value ?? string.Empty).Trim().Trim('"');
        return Regex.IsMatch(candidate, @"^\d{4,10}$") ? candidate : string.Empty;
    }
}
