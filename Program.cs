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

// DB initialization - run synchronously at startup to ensure tables exist
try
{
    var repository = app.Services.GetRequiredService<RubberJoins.Data.RubberJoinsRepository>();
    repository.InitializeAsync().GetAwaiter().GetResult();
    app.Logger.LogInformation("Database initialized successfully.");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to initialize database. App continues - use /api/init to retry.");
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

// Force init endpoint
app.MapGet("/api/init", async (RubberJoinsRepository repository) =>
{
    try
    {
        await repository.InitializeAsync();
        return Results.Json(new { success = true, message = "Init completed" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
    }
});

// Diagnostic endpoint to check DB state
app.MapGet("/api/diag", async (HttpContext context, RubberJoinsRepository repository) =>
{
    var results = new Dictionary<string, object>();
    try
    {
        var programs = await repository.GetProgramsAsync();
        results["programs"] = programs.Count;
        results["programNames"] = programs.Select(p => p.Name).ToList();
    }
    catch (Exception ex) { results["programs_error"] = ex.Message; }

    try
    {
        string userId = context.User.Identity?.Name ?? "anonymous";
        results["userId"] = userId;
        results["authenticated"] = context.User.Identity?.IsAuthenticated ?? false;
        var enrollment = await repository.GetActiveEnrollmentAsync(userId);
        results["enrollment"] = enrollment != null ? $"{enrollment.ProgramName} started {enrollment.StartDate}" : "none";
    }
    catch (Exception ex) { results["enrollment_error"] = ex.Message; }

    try
    {
        string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string userId = context.User.Identity?.Name ?? "anonymous";
        var plan = await repository.GetUserDailyPlanAsync(userId, todayDate);
        results["todayPlanEntries"] = plan.Count;
    }
    catch (Exception ex) { results["plan_error"] = ex.Message; }

    try
    {
        var exercises = await repository.GetAllExercisesAsync();
        results["exercises"] = exercises.Count;
    }
    catch (Exception ex) { results["exercises_error"] = ex.Message; }

    try
    {
        var steps = await repository.GetSessionStepsAsync("gym");
        results["gymSessionSteps"] = steps.Count;
    }
    catch (Exception ex) { results["sessionSteps_error"] = ex.Message; }

    return Results.Json(results);
});

app.Run();
