using Microsoft.EntityFrameworkCore;
using SaasEngine.Admin.Data;
using SaasEngine.Admin.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "SaasEngine.Admin")
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (builder.Environment.IsDevelopment()
        ? "Host=localhost;Port=5432;Database=saasengine;Username=saas_admin;Password=DevPassword123!"
        : throw new InvalidOperationException("DefaultConnection connection string is not configured."));
builder.Services.AddDbContext<AdminDbContext>(opts =>
    opts.UseNpgsql(connectionString));

builder.Services.AddProblemDetails();

var app = builder.Build();

// Auto-migrate and seed on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
    await SeedData.SeedAsync(db).ConfigureAwait(false);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.Run();

namespace SaasEngine.Admin
{
    public partial class Program { }
}
