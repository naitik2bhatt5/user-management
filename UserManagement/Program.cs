using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using UserManagement.Data;
using UserManagement.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Data access: plain ADO.NET with inline, parameterized SQL (no ORM).
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IStateRepository, StateRepository>();
builder.Services.AddScoped<IGeoProviderRepository, GeoProviderRepository>();

// Address autocomplete + validation. The active provider (Google, Amazon Location, ...) is chosen on the
// Providers screen (dbo.GeoProviders). Until a provider is saved there, the old GoogleMaps:ApiKey setting is used.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("UserManagement"); // encrypts API keys at rest
// Optional: keep the encryption key ring in a fixed folder (needed on a new/shared server, otherwise saved API keys
// become unreadable). Back this folder up. Without this setting the framework's per-user default location is used.
var keyRingPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
    if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(); // key files themselves are encrypted for this Windows machine
}

// Admin sign-in (cookie) for the Address providers page. Password comes from user secrets / environment, see AdminLogin.cs.
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddSingleton<IAdminLogin, AdminLogin>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "UserManagement.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // plain HTTP locally, secure when served over HTTPS
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.Configure<GoogleMapsOptions>(builder.Configuration.GetSection("GoogleMaps"));
builder.Services.AddHttpClient("address", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddScoped<IAddressProviderResolver, AddressProviderResolver>();

var app = builder.Build();

// Creates the database, tables and seed data on first run (needs only LocalDB, which ships with Visual Studio).
await DatabaseInitializer.InitializeAsync(app.Configuration, app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Users}/{action=Index}/{id?}");

app.Run();
