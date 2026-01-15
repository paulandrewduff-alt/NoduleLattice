// ============================================================================
// FILE: NoduleLattice.Api/Program.cs
// PURPOSE:
//   Register runner as hosted service + standard DI.
// ============================================================================
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoduleLattice.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Canon host
builder.Services.AddSingleton<LatticeHostService>();

// Real-time runner (hosted background service)
builder.Services.AddSingleton<LatticeRunnerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LatticeRunnerService>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { name = "NoduleLattice.Api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapControllers();

app.Run();
