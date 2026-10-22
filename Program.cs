using System.Threading.RateLimiting;
using CardTradeBuddy.Localization;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Development settings that must not be committed (the LoginServer client secret): this git-ignored file next to the project, or
// `dotnet user-secrets`. The file is read from the project folder, so it works however and by whoever the app is started.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages(options =>
{
    // The card scanner uses the GPU and the price services, so it is only for logged-in users.
    options.Conventions.AuthorizePage("/ProtoUpload");
}).AddViewLocalization();

// Login: this app has its own login page and checks passwords with the LoginServer (server to server).
builder.Services.Configure<LoginOptions>(builder.Configuration.GetSection(LoginOptions.Section));
builder.Services.AddHttpClient<LoginServerClient>((sp, client) =>
{
    var authority = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LoginOptions>>().Value.Authority;
    if (!string.IsNullOrWhiteSpace(authority))
        client.BaseAddress = new Uri(authority.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "cardtradebuddy.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Anti-forgery (CSRF): every Razor Page POST is validated automatically and the forms carry the token.
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "cardtradebuddy.antiforgery";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

// Slows down password guessing, sign-up spam and reset-mail flooding per visitor: at most RateLimit:AuthPerMinute requests a minute to the
// login and account pages from one address, 10 by default. (The LoginServer also locks an account after repeated failures.)
var authRequestsPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientIp.Get(context) ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHttpClient<ILlmService, OllamaLlmService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? OllamaLlmService.DefaultBaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5); // local vision models can be slow, especially on first load
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IExchangeRateService, FrankfurterExchangeRateService>(client =>
    client.BaseAddress = new Uri(FrankfurterExchangeRateService.BaseUrl));
builder.Services.AddHttpClient<ICardDataProvider, TcgDexCardDataProvider>(client =>
    client.BaseAddress = new Uri(TcgDexCardDataProvider.BaseUrl));

var app = builder.Build();

if (string.IsNullOrEmpty(app.Configuration[$"{LoginOptions.Section}:ClientSecret"]))
    app.Logger.LogWarning("Login:ClientSecret is not set, so logging in and creating users will not work. See the README, section Login.");

if (app.Environment.IsDevelopment())
{
    // Development only: show which LoginServer this run will use (no secret values), the first thing to check when a login fails.
    var login = app.Configuration.GetSection(LoginOptions.Section).Get<LoginOptions>() ?? new LoginOptions();
    app.Logger.LogInformation("Login settings: Authority='{Authority}', ClientId='{ClientId}', ClientSecret set: {HasSecret}, VerifyEmail: {VerifyEmail}",
        login.Authority, login.ClientId, login.ClientSecret.Length > 0, login.VerifyEmail);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Picks the language per request: ?culture=da, then the language cookie, then the browser's Accept-Language.
app.UseRequestLocalization(options => options
    .SetDefaultCulture(AppLanguages.Default)
    .AddSupportedCultures(AppLanguages.Codes)
    .AddSupportedUICultures(AppLanguages.Codes));

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
