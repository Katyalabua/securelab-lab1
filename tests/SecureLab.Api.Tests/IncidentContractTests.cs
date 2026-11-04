using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class IncidentContractTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_WithInvalidSeverity_Returns400WithSeverityKey()
    {
        var body = new
        {
            title = $"Regression-{Guid.NewGuid():N}",
            description = "Штучний опис.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        using var response = await _client.PostAsJsonAsync("/api/incidents", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("severity", out _));
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_DuplicateActiveTitle_Returns409()
    {
        var title = $"Regression-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Штучний опис.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(i => i.Title == title).ExecuteDeleteAsync();
        }
    }
}