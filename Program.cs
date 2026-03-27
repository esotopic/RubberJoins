using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
});

// Register RubberJoins Repository with connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "SET_IN_AZURE_APP_SETTINGS";
builder.Services.AddSingleton(new RubberJoins.Data.RubberJoinsRepository(connectionString));

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

// Auth middleware - order matters!
app.UseAuthentication();
app.UseAuthorization();

// DB initialization in background (non-blocking, per Azure playbook)
_ = Task.Run(async () =>
{
    try
    {
        var repository = app.Services.GetRequiredService<RubberJoins.Data.RubberJoinsRepository>();
        await repository.InitializeAsync();
        app.Logger.LogInformation("Database initialized successfully.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize database. App continues without DB.");
    }
});

app.MapRazorPages();

app.Run();
