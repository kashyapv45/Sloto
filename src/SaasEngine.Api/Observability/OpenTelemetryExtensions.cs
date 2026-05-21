using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SaasEngine.Api.Observability;

/// <summary>
/// Extension methods for configuring OpenTelemetry tracing and metrics.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>Adds OpenTelemetry tracing and metrics to the service collection.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static WebApplicationBuilder AddOpenTelemetryObservability(this WebApplicationBuilder builder)
    {
        var serviceName = "SaasEngine";
        var serviceVersion = typeof(OpenTelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(opts =>
                {
                    opts.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                .AddSource(serviceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(serviceName)
                .AddOtlpExporter());

        return builder;
    }
}
