using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    private static readonly Regex UserNamePattern = new(@"^[A-Za-z0-9\-._@+]{3,256}$");
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("Auth:LoginRateLimit:PermitLimit", 10),
                Window = TimeSpan.FromSeconds(config.GetValue("Auth:LoginRateLimit:WindowSeconds", 60)),
                QueueLimit = 0
            });
    });
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Auth event {EventType}: result {Result}, time {OccurredAtUtc}, trace {TraceId}",
            "login", "rate_limited", DateTimeOffset.UtcNow, context.HttpContext.TraceIdentifier);
        return ValueTask.CompletedTask;
    };
});
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
{
    var userName = input.UserName?.Trim();
    var email = input.Email?.Trim();
    var displayName = input.DisplayName?.Trim();
    var password = input.Password;

    if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(email)
        || string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(password))
        return Results.BadRequest();
    if (!UserNamePattern.IsMatch(userName) || email.Length > 256
        || displayName.Length > 120 || password.Length > 128)
        return Results.BadRequest(new { message = "Registration failed" });

    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        Email = email,
        DisplayName = displayName
    };
    var result = await users.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        var codes = result.Errors.Select(e => e.Code).ToArray();
        if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
            return Results.Json(new { message = "Identifier already in use" }, statusCode: 409);
        return Results.BadRequest(new { message = "Registration failed" });
    }
    var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
    if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
    return Results.Created("/api/me", new RegistrationResponse(user.Id, user.UserName!, user.DisplayName));
});
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn,
    HttpContext httpContext, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
    {
        LogLoginEvent(logger, httpContext, "failure");
        return Results.Unauthorized();
    }
    var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
    LogLoginEvent(logger, httpContext, result.Succeeded ? "success" : "failure");
    return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
}).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.NoContent();
});
        app.MapGet("/api/me", GetCurrentUser).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
    private static void LogLoginEvent(ILogger logger, HttpContext httpContext, string result) =>
    logger.LogInformation("Auth event {EventType}: result {Result}, time {OccurredAtUtc}, trace {TraceId}",
        "login", result, DateTimeOffset.UtcNow, httpContext.TraceIdentifier);
    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        return Results.Ok(new { UserId = verifiedUserId, UserName = userName, Role = role });
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);