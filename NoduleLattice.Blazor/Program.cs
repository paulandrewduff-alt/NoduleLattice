using NoduleLattice.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// .NET 9 Blazor Web App hosting
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// IMPORTANT: show the real circuit exception in the browser
builder.Services.AddServerSideBlazor().AddCircuitOptions(o =>
{
    o.DetailedErrors = true;
});

// Snapshot-only API client
builder.Services.AddHttpClient<LatticeApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["LatticeApi:BaseUrl"] ?? "http://localhost:7045/");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<NoduleLattice.Blazor.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
