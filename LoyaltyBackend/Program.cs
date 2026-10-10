using LoyaltyBackend.Services;
using LoyaltyBackend.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Load local path overrides before resolving the development gateway file.
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: true);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

// Reuse only the local gateway's session key in development. It stays server-side
// and is never copied into the repository. Explicit configuration takes priority.
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(builder.Configuration["APP_JWT_SECRET"]))
{
    var gatewayEnv = builder.Configuration["SharedGateway:EnvironmentFile"]
        ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../../EduvoBackend/.env"));
    if (File.Exists(gatewayEnv))
    {
        var line = File.ReadLines(gatewayEnv).FirstOrDefault(value => value.TrimStart().StartsWith("APP_JWT_SECRET=", StringComparison.Ordinal));
        if (line is not null) builder.Configuration["APP_JWT_SECRET"] = line[(line.IndexOf('=') + 1)..].Trim().Trim('"', '\'');
    }
}

// The website never requests or logs the provider JWT; the shared gateway owns it.

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

builder.Services.AddHttpClient("SharedGateway", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddTransient<SharedGatewayQrService>();
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
