using NoduleLattice.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Canon host
builder.Services.AddSingleton<LatticeHostService>();

// Real-time runner (depends on host)
builder.Services.AddSingleton<LatticeRunnerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Standard sanity endpoints
app.MapGet("/", () => Results.Ok(new { name = "NoduleLattice.Api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapControllers();

app.Run();
