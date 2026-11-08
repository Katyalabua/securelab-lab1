using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Login_ExceedingLimit_Returns429_ThenRecoversAfterWindow()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:LoginRateLimit:PermitLimit"] = "3",
                    ["Auth:LoginRateLimit:WindowSeconds"] = "5"
                })));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        for (var i = 0; i < 3; i++)
        {
            using var failed = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = "nobody-" + i, password = "Wr0ng-Pass!x" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = "Wr0ng-Pass!x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(5300));

        using var recovered = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = "Wr0ng-Pass!x" });
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }
}