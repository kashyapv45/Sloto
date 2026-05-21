using Serilog;
using Serilog.Formatting.Compact;

namespace SaasEngine.Api.Observability;

/// <summary>
/// Extension methods for configuring Serilog structured logging.
/// </summary>
public static class SerilogExtensions
{
    /// <summary>Configures Serilog with JSON console output and OpenTelemetry enrichment.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "SaasEngine")
            .WriteTo.Console(new RenderedCompactJsonFormatter())
            .CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }
}
