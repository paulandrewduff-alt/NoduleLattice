using NoduleLattice.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Lattice host (snapshot-only service facade)
builder.Services.AddSingleton<LatticeHostService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    env = app.Environment.EnvironmentName,
    timeUtc = DateTime.UtcNow
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
