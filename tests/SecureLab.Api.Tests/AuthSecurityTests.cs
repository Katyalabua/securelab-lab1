using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class AuthSecurityTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private static Dictionary<string, object?> NewIncident(string title, Guid? forgedOwner = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Тестовий опис інциденту",
            ["severity"] = "Low",
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        if (forgedOwner is not null) body["ownerUserId"] = forgedOwner;
        return body;
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetOwnerAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);
        return incident.OwnerUserId;
    }

    private async Task<HttpResponseMessage> RegisterAsync(string userName, string email, string password)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/register",
            new { userName, email, displayName = "Тест", password });
    }

    private async Task<HttpResponseMessage> AttemptLoginAsync(string userName, string password)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        return await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
    }

    [Fact]
    public async Task T01_Register_Valid_Returns201_WithoutSensitiveFields()
    {
        var name = Unique("reg");
        using var response = await RegisterAsync(name, name + "@example.test", Password);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        foreach (var forbidden in new[] { "password", "confirmPassword", "passwordHash", "securityStamp", "role", "token" })
            Assert.False(root.TryGetProperty(forbidden, out _), forbidden);
    }

    [Fact]
    public async Task T02_Register_WeakPassword_Returns400()
    {
        var name = Unique("reg");
        using var response = await RegisterAsync(name, name + "@example.test", "short");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task T02_Register_InvalidEmail_Returns400()
    {
        var name = Unique("reg");
        using var response = await RegisterAsync(name, "not-an-email", Password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_Duplicate_Returns409()
    {
        var name = Unique("reg");
        using var first = await RegisterAsync(name, name + "@example.test", Password);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var sameUserName = await RegisterAsync(name, "other-" + name + "@example.test", Password);
        Assert.Equal(HttpStatusCode.Conflict, sameUserName.StatusCode);

        using var sameEmail = await RegisterAsync("x" + name, name + "@example.test", Password);
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
    }

    [Fact]
    public async Task A01_Anonymous_CreateIncident_Returns401_NotRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsJsonAsync("/api/incidents", NewIncident(Unique("a01-")));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A02_Login_Me_ReturnsCurrentUser()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("userId").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        Assert.False(root.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task A03_ForeignDemoHeader_DoesNotChangeCurrentUser()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, doc.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task A04_Create_WithoutOwner_UsesPrincipalAsOwner()
    {
        using var client = await factory.LoginAsync("bob", Password);
        using var response = await client.PostAsJsonAsync("/api/incidents", NewIncident(Unique("a04-")));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await ReadIdAsync(response);
        Assert.Equal(DbSeeder.BobId, await GetOwnerAsync(id));
    }

    [Fact]
    public async Task A05_Create_WithForgedOwner_KeepsPrincipalAsOwner()
    {
        using var client = await factory.LoginAsync("bob", Password);
        using var response = await client.PostAsJsonAsync("/api/incidents",
            NewIncident(Unique("a05-"), forgedOwner: DbSeeder.AliceId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await ReadIdAsync(response);
        Assert.Equal(DbSeeder.BobId, await GetOwnerAsync(id));
    }

    [Fact]
    public async Task A06_Logout_ThenProtectedRequest_Returns401()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using (var before = await client.GetAsync("/api/me"))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A07_UnknownUser_And_WrongPassword_GiveSameExternalResult()
    {
        using var unknown = await AttemptLoginAsync(Unique("nobody-"), "Wr0ng-Pass!x");
        using var wrong = await AttemptLoginAsync("alice", "Wr0ng-Pass!x");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType, wrong.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await ProblemShape.ReadAsync(unknown), await ProblemShape.ReadAsync(wrong));
    }

    [Fact]
    public async Task Login_OverHttps_SetsExpectedCookieFlags()
    {
        using var response = await AttemptLoginAsync("alice", Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        const string cookieName = ".AspNetCore.Identity.Application=";
        var matching = response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(cookieName)).ToArray();
        Assert.True(matching.Length == 1);

        var attributes = matching[0].Split(';').Skip(1)
            .Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("expires") || a.StartsWith("max-age"));
    }
}