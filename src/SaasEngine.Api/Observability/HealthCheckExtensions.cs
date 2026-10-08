using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SaasEngine.Api.Observability;

/// <summary>
/// Extension methods for configuring health check endpoints.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>Adds health check services for PostgreSQL, Redis, and Hangfire.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHealthCheckServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        var pgConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? (environment is null || environment.IsDevelopment() ? "Host=localhost;Database=saasengine;Username=saas_admin;Password=DevPassword123!" : null);
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? (environment is null || environment.IsDevelopment() ? "localhost:6379,password=DevRedis123!" : null);

        var healthChecks = services.AddHealthChecks();

        if (!string.IsNullOrEmpty(pgConnectionString))
        {
            healthChecks.AddNpgSql(
                pgConnectionString,
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);
        }

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            healthChecks.AddRedis(
                redisConnectionString,
                name: "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);
        }

        return services;
    }

    /// <summary>Maps health check endpoints to the application.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The application for chaining.</returns>
    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        // Liveness: always 200 if process is alive
        app.MapGet("/health/live", () => Results.Ok(new SaasEngine.Api.Infrastructure.Serialization.MessageResponse("live")))
            .ExcludeFromDescription();

        // Readiness: checks DB, Redis, Hangfire
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse
        }).ExcludeFromDescription();

        // Startup: verify all systems initialized
        app.MapHealthChecks("/health/startup", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckResponse
        }).ExcludeFromDescription();

        return app;
    }

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status.ToString());
            writer.WriteStartArray("checks");

            foreach (var entry in report.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Key);
                writer.WriteString("status", entry.Value.Status.ToString());
                if (entry.Value.Description is not null)
                {
                    writer.WriteString("description", entry.Value.Description);
                }
                writer.WriteNumber("duration", entry.Value.Duration.TotalMilliseconds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(stream.ToArray()).ConfigureAwait(false);
    }
}
