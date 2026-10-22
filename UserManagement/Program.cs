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
builder.Services.AddDataProtection().SetApplicationName("UserManagement"); // encrypts API keys at rest
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Users}/{action=Index}/{id?}");

app.Run();
