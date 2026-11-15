using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class AuthorizationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    // Один login на користувача для всього класу: обмежувач спроб входу (ЛР 3) не пропускає десятки логінів.
    private static readonly ConcurrentDictionary<string, Lazy<Task<HttpClient>>> Clients = new();

    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private Task<HttpClient> Login(string userName) =>
        Clients.GetOrAdd(userName,
            name => new Lazy<Task<HttpClient>>(() => factory.LoginAsync(name, Password))).Value;

    private static object NewIncident(string title) => new
    {
        title,
        description = "тест авторизації",
        severity = "Low",
        occurredAtUtc = "2026-08-02T14:10:00+00:00"
    };

    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", NewIncident(title));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return Guid.Parse(body!["id"]!.GetValue<string>());
    }

    [Fact] // A-01
    public async Task Details_Anonymous_Returns401()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var response = await anonymous.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // A-02
    public async Task Details_AsOwner_ReturnsOk()
    {
        var alice = await Login("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // A-03
    public async Task Details_AsOtherReporter_Returns404_WithoutData()
    {
        var alice = await Login("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Боб Мельник", await response.Content.ReadAsStringAsync());
    }

    [Fact] // A-03R
    public async Task Details_AsAnalyst_ReturnsOk_WithInternalComment()
    {
        var morgan = await Login("morgan");
        using var response = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Внутрішня нотатка аналітика", await response.Content.ReadAsStringAsync());
    }

    [Fact] // A-04 update
    public async Task Update_AsNonOwner_Returns404_AndDoesNotChangeResource()
    {
        var alice = await Login("alice");
        var bob = await Login("bob");
        var before = await bob.GetFromJsonAsync<JsonObject>($"/api/incidents/{DbSeeder.BobIncidentId}");

        using var response = await alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}", NewIncident("ЗЛАМАНО"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var after = await bob.GetFromJsonAsync<JsonObject>($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(before!["title"]!.GetValue<string>(), after!["title"]!.GetValue<string>());
    }

    [Fact] // A-04 delete
    public async Task Delete_AsNonOwner_Returns404_AndResourceRemains()
    {
        var alice = await Login("alice");
        var bob = await Login("bob");
        using var response = await alice.DeleteAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var still = await bob.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact] // A-04: Analyst не має права update
    public async Task Update_AsAnalyst_Returns403_AndDoesNotChangeResource()
    {
        var alice = await Login("alice");
        var morgan = await Login("morgan");
        var id = await CreateAsync(alice, "A-04 analyst");
        try
        {
            using var response = await morgan.PutAsJsonAsync($"/api/incidents/{id}", NewIncident("ЗЛАМАНО"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var after = await alice.GetFromJsonAsync<JsonObject>($"/api/incidents/{id}");
            Assert.Equal("A-04 analyst", after!["title"]!.GetValue<string>());
        }
        finally
        {
            using var _ = await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact] // A-05
    public async Task List_AsReporter_DoesNotContainForeignIncidents()
    {
        var alice = await Login("alice");
        var raw = await alice.GetStringAsync("/api/incidents");
        Assert.Contains(DbSeeder.AliceIncidentId.ToString(), raw);
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), raw);
    }

    [Fact] // A-06
    public async Task Triage_AsReporter_Returns403_AndStatusUnchanged()
    {
        var alice = await Login("alice");
        var id = await CreateAsync(alice, "A-06");
        try
        {
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var after = await alice.GetFromJsonAsync<JsonObject>($"/api/incidents/{id}");
            Assert.Equal("New", after!["status"]!.GetValue<string>());
        }
        finally
        {
            using var _ = await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact] // A-07
    public async Task Triage_AsAnalyst_TriagesNewIncident()
    {
        var alice = await Login("alice");
        var morgan = await Login("morgan");
        var id = await CreateAsync(alice, "A-07");
        try
        {
            using var response = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await morgan.GetFromJsonAsync<JsonObject>($"/api/incidents/{id}");
            Assert.Equal("Triaged", after!["status"]!.GetValue<string>());
        }
        finally
        {
            using var _ = await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact] // A-08a
    public async Task Update_WithForgedFields_DoesNotChangeOwnerOrStatus()
    {
        var alice = await Login("alice");
        var id = await CreateAsync(alice, "A-08a");
        try
        {
            var before = await alice.GetFromJsonAsync<JsonObject>($"/api/incidents/{id}");
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = "нова назва",
                description = "d",
                severity = "Low",
                occurredAtUtc = "2026-08-02T14:10:00+00:00",
                ownerUserId = "10000000-0000-0000-0000-000000000002",
                status = "Triaged",
                role = "Administrator"
            });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var after = await alice.GetFromJsonAsync<JsonObject>($"/api/incidents/{id}");
            Assert.Equal("нова назва", after!["title"]!.GetValue<string>());
            Assert.Equal("New", after["status"]!.GetValue<string>());
            Assert.Equal(before!["ownerDisplayName"]!.GetValue<string>(),
                         after["ownerDisplayName"]!.GetValue<string>());
        }
        finally
        {
            using var _ = await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact] // A-10
    public async Task Details_ForeignAndUnknown_AreIndistinguishable()
    {
        var alice = await Login("alice");
        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());

        var foreignBody = await foreign.Content.ReadFromJsonAsync<JsonObject>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonObject>();
        foreignBody!.Remove("traceId");
        unknownBody!.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));
    }

    [Fact] // A-12 прямий child
    public async Task Comment_OfForeignIncident_Returns404()
    {
        var alice = await Login("alice");
        using var response = await alice.GetAsync(
            "/api/incident-comments/30000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // A-12 internal
    public async Task InternalComment_IsHiddenFromReporter_AndVisibleToAnalyst()
    {
        var alice = await Login("alice");
        var morgan = await Login("morgan");
        const string commentUrl = "/api/incident-comments/30000000-0000-0000-0000-000000000003";

        using var asReporter = await alice.GetAsync(commentUrl);
        Assert.Equal(HttpStatusCode.NotFound, asReporter.StatusCode);

        var detailsRaw = await alice.GetStringAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.DoesNotContain("Внутрішня нотатка аналітика", detailsRaw);

        using var asAnalyst = await morgan.GetAsync(commentUrl);
        Assert.Equal(HttpStatusCode.OK, asAnalyst.StatusCode);
    }
}
