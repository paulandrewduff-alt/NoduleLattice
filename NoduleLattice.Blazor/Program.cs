// ============================================================================
// FILE: NoduleLattice.Blazor/Program.cs
// PURPOSE:
//   Increase HttpClient timeout used by LatticeApiClient so long-running
//   lattice steps do not fail at 100 seconds.
// ============================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NoduleLattice.Blazor.Components;
using NoduleLattice.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor Components / Blazor
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// Typed API client with a longer timeout
builder.Services.AddHttpClient<LatticeApiClient>(client =>
{
    var baseUrl = builder.Configuration["LatticeApi:BaseUrl"] ?? "https://localhost:7045/";
    client.BaseAddress = new Uri(baseUrl);

    // IMPORTANT: default is 100 seconds; cortex stepping can exceed that.
    client.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
