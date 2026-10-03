using UserManagement.Data;
using UserManagement.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Data access: plain ADO.NET with inline, parameterized SQL (no ORM).
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IStateRepository, StateRepository>();

// Google address autocomplete + validation (optional: off when GoogleMaps:ApiKey is empty).
builder.Services.Configure<GoogleMapsOptions>(builder.Configuration.GetSection("GoogleMaps"));
builder.Services.AddHttpClient<GoogleAddressService>(c => c.Timeout = TimeSpan.FromSeconds(8));

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
