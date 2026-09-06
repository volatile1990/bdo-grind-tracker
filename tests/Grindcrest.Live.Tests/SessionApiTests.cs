using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Grindcrest.Api;
using Grindcrest.Live;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Grindcrest.Live.Tests;

public sealed class SessionApiTests
{
    [Fact]
    public async Task OwnersOnlyChangeTheirSessionAndKeysNeverAppearInListing()
    {
        await using var app = new ApiFixture();
        using var alice = app.Publisher(out var aliceKey);
        using var bob = app.Publisher(out _);
        using var anonymous = app.CreateClient();
        var session = Sample(app.Clock.GetUtcNow());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/session", session)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync("/api/v1/session", session)).StatusCode);
        await bob.DeleteAsync($"/api/v1/session/{session.SessionId}");
        Assert.Single((await anonymous.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
        var body = await anonymous.GetStringAsync("/api/v1/sessions");
        Assert.DoesNotContain(aliceKey, body);
        await alice.DeleteAsync($"/api/v1/session/{session.SessionId}");
        Assert.Empty((await anonymous.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
    }

    [Fact]
    public async Task EndBeforeDelayedFirstPutCannotResurrectSession()
    {
        await using var app = new ApiFixture();
        using var client = app.Publisher(out _);
        var session = Sample(app.Clock.GetUtcNow());
        await client.DeleteAsync($"/api/v1/session/{session.SessionId}");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/session", session)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
    }

    [Fact]
    public async Task PublisherCannotCopyAnotherPublicIdAndBreakTheListing()
    {
        await using var app = new ApiFixture();
        using var first = app.Publisher(out _);
        using var second = app.Publisher(out _);
        var session = Sample(app.Clock.GetUtcNow());
        await first.PutAsJsonAsync("/api/v1/session", session);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PutAsJsonAsync("/api/v1/session", session)).StatusCode);
        Assert.Single((await first.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
    }

    [Fact]
    public async Task OversizedAndInvalidFieldsAreRejected()
    {
        await using var app = new ApiFixture();
        using var client = app.Publisher(out _);
        var sample = Sample(app.Clock.GetUtcNow());
        foreach (var session in new[] {
            sample with { Loot = null! }, sample with { DisplayName = null! },
            sample with { ActiveSeconds = -1 }, sample with { SilverAfterTax = -1 },
            sample with { DisplayName = "Bad\nName" }, sample with { StartedAt = app.Clock.GetUtcNow().AddDays(1) }
        }) Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/session", session)).StatusCode);
        using var oversized = new StringContent("{\"padding\":\"" + new string('a', 70_000) + "\"}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PutAsync("/api/v1/session", oversized)).StatusCode);
    }

    [Fact]
    public async Task ExpiryUsesServerTimeAndPausedHeartbeatsCanResume()
    {
        await using var app = new ApiFixture();
        using var client = app.Publisher(out _);
        var session = Sample(app.Clock.GetUtcNow()) with { Paused = true };
        await client.PutAsJsonAsync("/api/v1/session", session);
        app.Clock.Advance(TimeSpan.FromSeconds(89));
        Assert.Single((await client.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty((await client.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/session", session with { Sequence = 2 })).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions);
    }

    [Fact]
    public async Task OldSequenceAndOlderPublicationCannotReplaceLatest()
    {
        await using var app = new ApiFixture();
        using var client = app.Publisher(out _);
        var first = Sample(app.Clock.GetUtcNow());
        await client.PutAsJsonAsync("/api/v1/session", first);
        await client.PutAsJsonAsync("/api/v1/session", first with { Sequence = 5, ActiveSeconds = 123 });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/session", first with { Sequence = 4 })).StatusCode);
        var newer = first with { SessionId = Guid.NewGuid(), SharedAt = first.SharedAt.AddSeconds(1) };
        await client.PutAsJsonAsync("/api/v1/session", newer);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/session", first with { Sequence = 6 })).StatusCode);
        await client.DeleteAsync($"/api/v1/session/{first.SessionId}");
        Assert.Equal(newer.SessionId, (await client.GetFromJsonAsync<LiveSessionList>("/api/v1/sessions"))!.Sessions.Single().Session.SessionId);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"loot\":null}")]
    [InlineData("{\"displayName\":null}")]
    public async Task InvalidJsonIsRejectedWithoutServerError(string json)
    {
        await using var app = new ApiFixture();
        using var client = app.Publisher(out _);
        var result = await client.PutAsync("/api/v1/session", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task PublicCorsAllowsOnlyConfiguredOriginAndResponsesAreNotCached()
    {
        await using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions");
        allowed.Headers.Add("Origin", "https://volatile1990.github.io");
        var response = await client.SendAsync(allowed);
        Assert.Equal("https://volatile1990.github.io", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var forbidden = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions");
        forbidden.Headers.Add("Origin", "https://unrelated.example");
        Assert.False((await client.SendAsync(forbidden)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task InvalidTokensAlsoReachRateLimit()
    {
        await using var app = new ApiFixture();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('a', 64));
        HttpResponseMessage? result = null;
        for (var i = 0; i < 241; i++)
        {
            result?.Dispose();
            result = await client.DeleteAsync($"/api/v1/session/{Guid.Empty}");
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, result!.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), result.Headers.RetryAfter!.Delta);
        result.Dispose();
    }

    [Fact]
    public void StoreSurvivesRestartAndRevocationRemovesAccessAndSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "grindcrest-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "test.db");
            var store = new SessionStore(path);
            var key = store.IssueKey("Test");
            var now = DateTimeOffset.UtcNow;
            store.Upsert(key.Id, Sample(now), now);
            var reopened = new SessionStore(path);
            Assert.Equal(key.Id, reopened.Authenticate(key.Token));
            Assert.Single(reopened.List(now));
            Assert.True(reopened.RevokeKey(key.Id));
            Assert.Null(reopened.Authenticate(key.Token));
            Assert.Empty(reopened.List(now));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    internal static LiveSessionUpdate Sample(DateTimeOffset now) => new()
    {
        SessionId = Guid.NewGuid(), Sequence = 1, DisplayName = "Testspieler", Region = "EU",
        StartedAt = now.AddHours(-1), SharedAt = now, ActiveSeconds = 120, SilverAfterTax = 123_456,
        Loot = new() { ["Black Stone"] = 8 }
    };
}

internal sealed class ApiFixture : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grindcrest-api-test-" + Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Live:DatabasePath", Path.Combine(_directory, "test.db"));
        builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock); });
    }
    public HttpClient Publisher(out string token)
    {
        var client = CreateClient();
        var key = Services.GetRequiredService<SessionStore>().IssueKey("Test");
        token = key.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _now.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan by) => _now += by;
}
