using BdoGrindTracker.App;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using BdoGrindTracker.BrowserPreview;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});
builder.WebHost.UseUrls("http://127.0.0.1:5180");
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider().DisableAutomaticKeyGeneration();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// A circuit owns its demo data. Separate browser tabs never share edits.
builder.Services.AddScoped<ITrackerSession>(_ => new PreviewTrackerSession());
builder.Services.AddScoped(_ => new GrindGoalStore(null));
builder.Services.AddScoped<IOverlayService>(services => new OverlayService(
    services.GetRequiredService<ITrackerSession>(), goals: services.GetRequiredService<GrindGoalStore>()));
builder.Services.AddScoped<IAppUpdates>(_ => new DisabledAppUpdates(AppBranding.Version,
    "Browser-Vorschau · App-Updates sind deaktiviert."));

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<PreviewDocument>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
