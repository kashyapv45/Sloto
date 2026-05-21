using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SaasEngine.Domain.Billing;
using SaasEngine.Domain.Shared;

namespace SaasEngine.Api.Infrastructure.Serialization;

/// <summary>
/// Intercepts unhandled exceptions globally and maps them to standard HTTP response status codes and Problem Details.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GlobalExceptionHandler"/>.
    /// </summary>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is PlanLimitExceededException planLimitExceeded)
        {
            _logger.LogWarning(exception, "Plan limit exceeded: {Message}", exception.Message);

            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (planLimitExceeded.RetryAfterSeconds.HasValue)
            {
                httpContext.Response.Headers["Retry-After"] = planLimitExceeded.RetryAfterSeconds.Value.ToString();
            }

            var problem = new ProblemDetails
            {
                Title = "Plan Limit Exceeded",
                Detail = planLimitExceeded.Message,
                Status = StatusCodes.Status429TooManyRequests,
                Type = "https://tools.ietf.org/html/rfc7807"
            };
            problem.Extensions["limitType"] = planLimitExceeded.LimitType;

            httpContext.Response.ContentType = "application/problem+json";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                problem,
                AppJsonSerializerContext.Default.ProblemDetails,
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        if (exception is FeatureDisabledException featureDisabled)
        {
            _logger.LogWarning(exception, "Feature disabled check failed: {Message}", exception.Message);

            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;

            var problem = new ProblemDetails
            {
                Title = "Feature Disabled",
                Detail = featureDisabled.Message,
                Status = StatusCodes.Status403Forbidden,
                Type = "https://tools.ietf.org/html/rfc7807"
            };
            problem.Extensions["featureKey"] = featureDisabled.FeatureKey;

            httpContext.Response.ContentType = "application/problem+json";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                problem,
                AppJsonSerializerContext.Default.ProblemDetails,
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        if (exception is SaasEngine.Api.Features.Identity.DuplicateEmailException duplicateEmail)
        {
            _logger.LogWarning(exception, "Registration conflict: {Message}", exception.Message);

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

            var problem = new ProblemDetails
            {
                Title = "Conflict",
                Detail = duplicateEmail.Message,
                Status = StatusCodes.Status409Conflict,
                Type = "https://tools.ietf.org/html/rfc7807"
            };

            httpContext.Response.ContentType = "application/problem+json";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                problem,
                AppJsonSerializerContext.Default.ProblemDetails,
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);
        return false;
    }
}
