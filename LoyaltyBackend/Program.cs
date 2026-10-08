using LoyaltyBackend.Services;
using LoyaltyBackend.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Keep local API credentials available for every launch profile, including when
// the compiled application is started directly. The values remain outside source
// control in .NET User Secrets.
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    // Local fallback for IDEs that do not resolve the User Secrets provider.
    // This file is excluded by .gitignore and must never be committed.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

// The provider's JWT endpoint requires credentials in its query string.
// Prevent the default HttpClient logger from writing that URL to application logs.
builder.Logging.AddFilter("System.Net.Http.HttpClient.LoyaltyApi", LogLevel.Warning);

// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<ApiExceptionFilter>());
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "Eduvo.Member.Flow";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Supports the assignment's local HTTP profile while still marking the
    // cookie secure whenever the request itself uses HTTPS.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("account", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "Eduvo.Member";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddOptions<LoyaltyApiOptions>()
    .Bind(builder.Configuration.GetSection(LoyaltyApiOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
        "LoyaltyApi:BaseUrl must be a valid absolute URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.JwtUserName),
        "LoyaltyApi:JwtUserName must be configured through User Secrets or environment variables.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.JwtPassword),
        "LoyaltyApi:JwtPassword must be configured through User Secrets or environment variables.")
    .ValidateOnStart();
builder.Services.AddHttpClient("LoyaltyApi", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<ILoyaltyTokenProvider, LoyaltyTokenProvider>();
builder.Services.AddTransient<ILoyaltyApiClient, LoyaltyApiClient>();
builder.Services.AddTransient<ILoyaltyMemberService, LoyaltyMemberService>();
builder.Services.AddTransient<ILoyaltyFeatureService, LoyaltyFeatureService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
