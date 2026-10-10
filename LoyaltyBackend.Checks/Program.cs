using System.Net;
using System.Reflection;
using LoyaltyBackend.Services;
using LoyaltyBackend.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); passed++; }
var api = new FakeApi();
Check(!MemberPhoneIdentity.IsValid("11"), "Member ID 11 cannot be used as a phone-number identity");
Check(MemberPhoneIdentity.IsValid("+600000000001") && MemberPhoneIdentity.IsValid("0123456789"), "Valid member phone-number identities remain supported");
using (var rewardPayload = JsonDocument.Parse(MemberQrImage.RewardPayload("+600000000001", "Reward-1", true, "secure-grant")))
{
    var root = rewardPayload.RootElement;
    Check(root.GetProperty("t").GetString() == "R" && root.GetProperty("p").GetString() == "+600000000001"
        && root.GetProperty("r").GetString() == "Reward-1" && root.GetProperty("k").GetString() == "v"
        && root.GetProperty("v").GetString() == "secure-grant", "Website reward QR matches mobile payload format");
}
Check(MemberQrImage.Render("secure-member-grant").Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Member QR renders as a PNG");
Check(MemberQrImage.Render(MemberQrImage.RewardPayload("+600000000001", "Reward-1", false, "secure-grant"), true).Length > 100,
    "Reward QR renders the mobile-compatible envelope");
api.Data["api/MemberDetails/GetMemberDetails"] = """{"Name":"Sample Member","Balance":0,"Point":0,"TotalStamp":0}""";
api.Data["api/MemberWallet/MemberGetWalletDetails"] = """{"Balance":92.3,"Point":1581,"TotalStamp":9,"Tier":"Platinum"}""";
var summary = await new LoyaltyMemberService(api).GetSummaryAsync("+600000000001");
Check(summary.Name == "Sample Member" && summary.WalletBalance == 92.3m && summary.Points == 1581 && summary.Stamps == 9, "Dashboard uses authoritative wallet balances");
var features = new LoyaltyFeatureService(api);
api.Data["api/ManageOutlets/GetAllOutlets"] = """[{"Name":"Open outlet","Status":"Active"},{"Name":"Closed outlet","Status":"Inactive"}]""";
Check((await features.GetOutletsAsync()).Select(item => item.Name).SequenceEqual(["Open outlet"]), "Inactive outlets are excluded from member stores");
api.Data["api/MerchantTransactionHistories/GetAllRewardsRecordsFilterByMember"] = """[{"Id":30,"Type":"Reward","Description":"Tea","RedeemDate":"2026-10-10T12:24:58","Phone":"+600000000001"}]""";
api.Data["api/MerchantTransactionHistories/GetAllVoucherRecordsFilterByMember"] = """[{"Id":31,"Type":"Voucher","Description":"Counter treat","RedeemDate":"2026-10-10T12:27:26","Phone":"+600000000001"}]""";
var rewardHistory = await features.GetHistoryAsync("+600000000001", "Reward");
Check(rewardHistory.Count == 1 && rewardHistory[0].OccurredAt.Year == 2026, "Official reward history uses RedeemDate");
var voucherHistory = await features.GetHistoryAsync("+600000000001", "Voucher");
Check(voucherHistory.Count == 1 && voucherHistory[0].OccurredAt.Minute == 27, "Official voucher history uses RedeemDate");
api.Data["api/History/GetAllRecordByPhoneNumber"] = """[{"Type":"Redeem Reward (Tea)","Description":"Done used 10 to redeem reward","DateTime":"2026-10-10T12:24:58.2006999","PhoneNumber":"+600000000001"},{"Type":"Redeem Voucher (Counter treat)","DateTime":"2026-10-10T12:27:26.5499745","PhoneNumber":"+600000000001"}]""";
Check((await features.GetHistoryAsync("+600000000001", "all")).Count == 2, "All history reconciles provider descriptions and sub-second timestamps without duplicate redemptions");
api.Data["api/History/GetAssignPointRecordByPhoneNumber"] = """[{"Point":2,"DateAssign":"2026-10-09T15:00:00","ReferenceNumber":"BONUS"}]""";
api.Data["api/MemberAccount/GetSpendRecords"] = """[{"Status":"Paid","RewardPoint":8,"SpendTime":"2026-10-09T16:00:00","ReferenceNumber":"SALE-1"},{"Status":"Void","RewardPoint":8,"ReferenceNumber":"SALE-VOID"}]""";
var points = await features.GetHistoryAsync("+600000000001", "Points");
Check(points.Count == 2 && points[0].Points == 8 && points[1].Points == 2, "Purchase points included; void points excluded; newest first");
api.Data["api/History/GetAssignPointRecordByPhoneNumber"] = """[{"Point":8,"ReferenceNumber":"SALE-1"}]""";
Check((await features.GetHistoryAsync("+600000000001", "Points")).Count == 1, "Purchase points are not displayed twice");
api.Data["api/History/GetStampRecordByPhoneNumber"] = """[{"TotalUseStamp":3,"ClaimDate":"2026-10-09T16:00:00"}]""";
var stamps = await features.GetHistoryAsync("+600000000001", "Stamps used");
Check(stamps[0].Stamps == -3 && stamps[0].Points is null && stamps[0].OccurredAt.Year == 2026, "Stamp usage has signed count and claim date");
api.Data["api/History/GetPaymentRecordByPhoneNumber"] = """[{"Type":"Payment","Amount":5.9,"Point":0,"ReferenceNumber":"SALE-1"}]""";
var payments = await features.GetHistoryAsync("+600000000001", "Wallet");
Check(payments[0].Amount == -5.9m && payments[0].Points == 8, "Payment shows spend amount and earned points");
api.Data["api/MemberNotification/GetNotificationsFilterMember"] = """[{"NotificationId":"N1","Title":"Receipt","IsUnread":1}]""";
Check((await features.GetNotificationsAsync("+600000000001"))[0].IsUnread, "Numeric unread flag is supported");
api.Data["api/MemberNotification/UserReadNotification"] = "\"Send Failed\"";
var rejected = false;
try { await features.MarkNotificationReadAsync("+600000000001", "N1"); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "HTTP-200 action failure is not treated as success");
Check(!new RewardCard(1, "Voucher", "Voucher", 0, "", true, "R1", DateTime.Now.AddDays(-1)).CanRedeem, "Expired item cannot display redemption QR");
Check(!new RewardCard(1, "Voucher", "Voucher", 0, "", true, "R1", null, null, "Used").CanRedeem, "Used item cannot display redemption QR");
Check(new RewardCard(1, "Voucher", "Voucher", 0, "", true, "R1", DateTime.Now.AddDays(1), null, "Active").CanRedeem, "Active unexpired item remains eligible");
var method = typeof(LoyaltyBackend.Controllers.AccountController).GetMethod("HasMemberRecordAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
using var notFound = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Member Not Found With Email") };
Check(!await (Task<bool>)method.Invoke(null, [notFound, CancellationToken.None])!, "Unused email lookup is not a registration error");
var gateway = new FakeGateway();
var qr = new SharedGatewayQrService(gateway, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["APP_JWT_SECRET"] = "checks-only-session-secret" }).Build());
Check(await qr.CreateAsync("+600000000001") == "short-lived-grant", "QR uses grant returned by the shared backend");
var jwtParts = gateway.Authorization!.Split(' ')[1].Split('.');
var payload = jwtParts[1].Replace('-', '+').Replace('_', '/');
using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
Check(claims.RootElement.GetProperty("phoneNumber").GetString() == "+600000000001" && claims.RootElement.GetProperty("role").GetString() == "member", "QR service session is scoped to the signed-in member");
await qr.CreateAsync("+600000000001", "Reward-1", true);
Check(gateway.Path!.EndsWith("/rewards/Reward-1/qr?kind=voucher"), "Voucher QR uses the matching shared gateway route");
var broker = new LoyaltyApiClient(gateway, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["APP_JWT_SECRET"] = "checks-only-session-secret" }).Build());
using var brokerResponse = await broker.PostAsync("api/MemberWallet/MemberGetWalletDetails", new { PhoneNumber = "+600000000001" });
Check(gateway.Path == "/web/member-api" && gateway.Body!.Contains("MemberWallet/MemberGetWalletDetails") && !gateway.Body.Contains("api/MemberWallet"), "Member calls use the shared broker with normalized official endpoints");
var brokerParts = gateway.Authorization!.Split(' ')[1].Split('.');
var brokerPayload = brokerParts[1].Replace('-', '+').Replace('_', '/');
using var brokerClaims = JsonDocument.Parse(Convert.FromBase64String(brokerPayload.PadRight((brokerPayload.Length + 3) / 4 * 4, '=')));
Check(brokerClaims.RootElement.GetProperty("role").GetString() == "web-service", "Broker uses a server-only service role");
Check(VerificationChallenge.IsValid("123456", "123456", 101, 100), "Correct unexpired verification code is accepted");
Check(!VerificationChallenge.IsValid("123456", "000000", 101, 100), "Incorrect verification code is rejected before upstream login");
Check(!VerificationChallenge.IsValid("123456", "123456", 100, 100), "Expired verification challenge is rejected");
Check(!VerificationChallenge.IsValid(null, null, 101, 100), "Missing verification challenge cannot complete a flow");
using var brokerBody = JsonDocument.Parse(gateway.Body!);
Check(brokerBody.RootElement.GetProperty("payload").TryGetProperty("PhoneNumber", out _), "Outgoing payload preserves exact Swagger field casing");
var referralMethod = typeof(LoyaltyBackend.Controllers.AccountController).GetMethod("IsValidReferralResult", BindingFlags.Static | BindingFlags.NonPublic)!;
Check(!(bool)referralMethod.Invoke(null, ["\"Referral Code Doesn't Exist\""])!, "HTTP-200 nonexistent referral is rejected");
Check((bool)referralMethod.Invoke(null, ["\"Referral Code Exist\""])!, "Confirmed existing referral is accepted");
Check(!(bool)referralMethod.Invoke(null, ["unexpected result"])!, "Unknown referral response fails closed");
Console.WriteLine($"{passed} checks passed.");

sealed class FakeApi : ILoyaltyApiClient
{
    public Dictionary<string, string> Data { get; } = new();
    public Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default) => Respond(endpoint);
    public Task<HttpResponseMessage> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default) => Respond(endpoint);
    private Task<HttpResponseMessage> Respond(string endpoint) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Data.GetValueOrDefault(endpoint, "[]")) });
}

sealed class FakeGateway : HttpMessageHandler, IHttpClientFactory
{
    public string? Authorization { get; private set; }
    public string? Path { get; private set; }
    public string? Body { get; private set; }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorization = request.Headers.Authorization?.ToString();
        Path = request.RequestUri?.PathAndQuery;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"qrToken":"short-lived-grant","expiresInSeconds":300}""") };
    }
}
