# Eduvo Loyalty Member Website

ASP.NET Core MVC member portal for the Loyalty App V2 API. The browser talks only to this server; API JWT credentials and tokens are never sent to browser code.

## Implemented member features

- Email/password and phone-OTP login, registration and password reset
- Secure cookie sessions and account-endpoint rate limiting
- Live member card, wallet, points, stamps, category-filtered activity and promotional highlights
- Reward and voucher catalogues with details, eligible redemption QR codes, claimed-item status and member QR
- Notification inbox/details, referrals with copy/share, outlets with OpenStreetMap, profile editing and photo upload
- Email verification, feedback history/submission and account deactivation
- Friendly upstream-service error page and `/health` endpoint

Food ordering is intentionally excluded because the supplied API does not document an online-payment operation.

## Local setup

Requires the .NET 8 SDK and the running shared EduvoBackend. Configure its matching server session key with .NET User Secrets; do not add it to `appsettings.json`:

```powershell
dotnet user-secrets set "SharedGateway:SessionSecret" "YOUR_SHARED_BACKEND_SESSION_KEY"
dotnet user-secrets set "SharedGateway:BaseUrl" "http://localhost:3000"
dotnet restore
dotnet run
```

Xcode JWT username/password belong only in the shared backend configuration. The website does not need separate provider credentials. User Secrets are local to each developer.

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
- Member and reward QRs now use the same short-lived grants from the shared mobile/POS backend. Run that backend before opening a QR. Codes refresh while the page is visible and when it returns from the background.
- Several endpoints return undocumented plain-text empty states; the integration normalizes these safely.
- JWT credentials are query parameters in the provider API, so HTTP-client URL logging is suppressed.

## Mobile and POS alignment

Dashboard and wallet balances use `MemberWallet/MemberGetWalletDetails`; member identity still comes from `MemberDetails/GetMemberDetails`. Dashboard and wallet totals refresh every 10 seconds while visible. History reconciles purchase `RewardPoint` from `MemberAccount/GetSpendRecords` without writing duplicate point records and excludes voided purchases from earned points. Stamp usage, spending and voucher-activity filters are available.

The MVC web server requests QR grants from `SharedGateway:BaseUrl` (default `http://localhost:3000`). Configure `SharedGateway:SessionSecret` through User Secrets or supply `APP_JWT_SECRET`, matching the shared backend. In this development clone layout only, the server can read that key from `../../../EduvoBackend/.env`; configure `SharedGateway:EnvironmentFile` to override the location. No signing key or upstream JWT is sent to the browser.

Web OTP login reuses the registered phone DeviceId and does not explicitly register a browser for push. All MVC account and feature calls now use the restricted `/web/member-api` adapter in the shared gateway, authenticated with a short-lived server-only service session. Only explicitly allowlisted member endpoints can be called; ordinary member and merchant sessions cannot use this adapter. The gateway owns the Xcode API token used by mobile, POS and this member webpage. Legacy token-provider source is retained but is not registered or used at runtime.

Regression checks: `dotnet run --project ../LoyaltyBackend.Checks/LoyaltyBackend.Checks.csproj`. These use synthetic API responses, never live transactions. The authenticated smoke script remains read-only.

History and stamp lists initially display eight entries per section, with Load more controls. The upstream API still returns complete lists; the website limits the rendered records. Claimed reward records are displayed as claimed items rather than attempting to generate a new redemption QR from a history-record ID.
