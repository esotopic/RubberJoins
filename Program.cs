using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text.Json;
using RubberJoins.Data;

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

// DB initialization with retry (Azure SQL Serverless may be paused)
{
    var repository = app.Services.GetRequiredService<RubberJoins.Data.RubberJoinsRepository>();
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            repository.InitializeAsync().GetAwaiter().GetResult();
            app.Logger.LogInformation("Database initialized successfully on attempt {Attempt}.", attempt);
            break;
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "DB init attempt {Attempt} failed.", attempt);
            if (attempt < 3) Thread.Sleep(5000);
        }
    }
}

app.MapRazorPages();

// ── Minimal API endpoints (bypass Razor Pages routing for reliable JSON responses) ──

app.MapPost("/api/check", async (HttpContext context, RubberJoinsRepository repository) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    string userId = context.User.Identity?.Name ?? "default";
    string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string itemType = root.GetProperty("itemType").GetString() ?? "";
        string itemId = root.GetProperty("itemId").GetString() ?? "";
        int stepIndex = root.TryGetProperty("stepIndex", out var si) ? si.GetInt32() : 0;
        bool checkedState = root.TryGetProperty("checked", out var cp) && cp.GetBoolean();

        await repository.SetCheckAsync(userId, todayDate, itemType, itemId, stepIndex, checkedState);

        return Results.Json(new { success = true, userId, todayDate, itemType, itemId, stepIndex, checkedState });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/api/debug", async (HttpContext context, RubberJoinsRepository repository) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    string userId = context.User.Identity?.Name ?? "default";
    string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

    try
    {
        var checks = await repository.GetDailyChecksAsync(userId, todayDate);
        return Results.Json(new
        {
            userId,
            todayDate,
            utcNow = DateTime.UtcNow.ToString("o"),
            checksCount = checks.Count,
            checks = checks.Select(c => new { c.ItemType, c.ItemId, c.StepIndex, c.Checked })
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/milestone", async (HttpContext context, RubberJoinsRepository repository) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    string userId = context.User.Identity?.Name ?? "default";

    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string id = root.GetProperty("id").GetString() ?? "";
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await repository.CompleteUserMilestoneAsync(userId, id, today);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/logsession", async (HttpContext context, RubberJoinsRepository repository) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Json(new { success = false, error = "not authenticated" });

    string userId = context.User.Identity?.Name ?? "default";
    string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

    try
    {
        // Use UserDailyPlan instead of SessionSteps
        var planEntries = await repository.GetUserDailyPlanAsync(userId, todayDate);
        var settings = await repository.GetUserSettingsAsync(userId);
        var disabledToolIds = (settings?.DisabledTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        int totalSteps = planEntries.Count(e => !disabledToolIds.Contains(e.ExerciseId));
        var dailyChecks = await repository.GetDailyChecksAsync(userId, todayDate);
        int completedSteps = dailyChecks.Count(c => c.ItemType == "step" && c.Checked);

        await repository.LogSessionAsync(userId, todayDate, completedSteps, totalSteps);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
    }
});

app.Run();
