using System.Threading.RateLimiting;
using Grindcrest.Api;
using Grindcrest.Live;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65_536);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new SessionStore(
    builder.Configuration["Live:DatabasePath"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "live.db")));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Live:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).WithMethods("GET").WithHeaders("Content-Type");
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Items["publisher"]?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetConcurrencyLimiter("api", _ => new() { PermitLimit = 100, QueueLimit = 0 }));
});

var app = builder.Build();
var store = app.Services.GetRequiredService<SessionStore>();
// Operator-only provisioning, no public registration endpoint or global key in clients.
if (args.Length == 2 && args[0] == "--issue-key")
{
    var key = store.IssueKey(args[1]);
    Console.WriteLine($"Publisher: {key.Id}\nWrite token (only shown here): {key.Token}");
    return;
}
if (args.Length == 2 && args[0] == "--revoke-key")
{
    Console.WriteLine(store.RevokeKey(args[1]) ? "Revoked." : "Publisher not found.");
    return;
}

var trustedProxies = builder.Configuration.GetSection("Live:TrustedProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var proxy in trustedProxies) forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    app.UseForwardedHeaders(forwarded);
}
// Bound requests before any database/token lookup, including failed authentication.
using var ingress = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
using var concurrentRequests = new ConcurrencyLimiter(new() { PermitLimit = 100, QueueLimit = 0 });
app.Use(async (context, next) =>
{
    using var concurrencyLease = await concurrentRequests.AcquireAsync(1, context.RequestAborted);
    using var rateLease = await ingress.AcquireAsync(context, 1, context.RequestAborted);
    if (!concurrencyLease.IsAcquired || !rateLease.IsAcquired)
    {
        context.Response.StatusCode = 429;
        context.Response.Headers.RetryAfter = "60";
        return;
    }
    await next(context);
});

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
    await context.Response.WriteAsJsonAsync(new { error = context.Response.StatusCode < 500
        ? "Anfragedaten ungültig." : "Die Session-API ist vorübergehend nicht verfügbar." });
}));
app.UseCors();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.ContentLength > 65_536)
    {
        context.Response.StatusCode = 413;
        return;
    }
    if (context.Request.Method is "PUT" or "DELETE")
    {
        var header = context.Request.Headers.Authorization.ToString();
        var owner = store.Authenticate(header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null);
        if (owner is null)
        {
            context.Response.StatusCode = 401;
            return;
        }
        context.Items["publisher"] = owner;
    }
    await next(context);
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/v1/sessions", (TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    return Results.Ok(new LiveSessionList(now, store.List(now)));
}).RequireRateLimiting("read");
app.MapPut("/api/v1/session", (LiveSessionUpdate? session, HttpContext context, TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    if (session is null) return Results.BadRequest(new { error = "Sessiondaten fehlen." });
    if (session.Validate(now) is { } error) return Results.BadRequest(new { error });
    return store.Upsert((string)context.Items["publisher"]!, session, now)
        ? Results.NoContent() : Results.Conflict(new { error = "Sitzung beendet oder Update überholt." });
}).RequireRateLimiting("write");
app.MapDelete("/api/v1/session/{sessionId:guid}", (Guid sessionId, HttpContext context, TimeProvider clock) =>
{
    store.End((string)context.Items["publisher"]!, sessionId, clock.GetUtcNow());
    return Results.NoContent();
}).RequireRateLimiting("write");
app.Run();

public partial class Program;
