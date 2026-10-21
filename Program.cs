using CardTradeBuddy.Localization;
using CardTradeBuddy.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages().AddViewLocalization();
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

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
