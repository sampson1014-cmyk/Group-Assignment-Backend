# Eduvo Loyalty Member Website

ASP.NET Core MVC member portal for the Loyalty App V2 API. The browser talks only to this server; API JWT credentials and tokens are never sent to browser code.

## Implemented member features

- Email/password and phone-OTP login, registration and password reset
- Secure cookie sessions and account-endpoint rate limiting
- Live member card, wallet, points, stamps, category-filtered activity and promotional highlights
- Reward and voucher catalogues with details, owned-item redemption QR codes and member QR
- Notification inbox/details, referrals with copy/share, outlets with OpenStreetMap, profile editing and photo upload
- Email verification, feedback history/submission and account deactivation
- Friendly upstream-service error page and `/health` endpoint

Food ordering is intentionally excluded because the supplied API does not document an online-payment operation.

## Local setup

Requires the .NET 8 SDK. Configure API-service credentials with .NET User Secrets; do not add them to `appsettings.json`:

```powershell
dotnet user-secrets set "LoyaltyApi:JwtUserName" "YOUR_API_USERNAME"
dotnet user-secrets set "LoyaltyApi:JwtPassword" "YOUR_API_PASSWORD"
dotnet restore
dotnet run
```

`LoyaltyApi:BaseUrl` is configured in `appsettings.json`. User Secrets are local to each developer, so every teammate must configure them separately.

## Verification

Build:

```powershell
dotnet build
```

Run the non-mutating authenticated smoke test against a disposable member account:

```powershell
.\scripts\SmokeTest.ps1 -BaseUrl "https://localhost:PORT" -Email "TEST_MEMBER_EMAIL" -Password "TEST_MEMBER_PASSWORD"
```

The script checks all member pages and QR rendering. It does not edit profiles, submit feedback, mark notifications, reset passwords or deactivate accounts.

## Known upstream limitations

- The API exposes member passwords in some responses. This portal deliberately ignores that field.
- The API supports account deactivation, not permanent deletion.
- Member QR payload is the registered phone number because the POS scan endpoint accepts a phone number.
- Several endpoints return undocumented plain-text empty states; the integration normalizes these safely.
- JWT credentials are query parameters in the provider API, so HTTP-client URL logging is suppressed.
