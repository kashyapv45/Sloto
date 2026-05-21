using System.Text.Json.Serialization;
using SaasEngine.Contracts.Identity;
using SaasEngine.Contracts.Tenancy;

namespace SaasEngine.Api.Infrastructure.Serialization;

/// <summary>
/// Error response object returned by API endpoints.
/// </summary>
public sealed record ErrorResponse(string Message);

/// <summary>
/// Simple message response returned by API endpoints.
/// </summary>
public sealed record MessageResponse(string Message);

/// <summary>
/// Native AOT compatible JSON serialization context for the entire application.
/// </summary>
[JsonSerializable(typeof(CreateTenantRequest))]
[JsonSerializable(typeof(TenantResponse))]
[JsonSerializable(typeof(UpdateTenantStatusRequest))]
[JsonSerializable(typeof(UpdateTenantPlanRequest))]
[JsonSerializable(typeof(AuthTokenRequest))]
[JsonSerializable(typeof(AuthTokenResponse))]
[JsonSerializable(typeof(MfaEnrollResponse))]
[JsonSerializable(typeof(MfaVerifyRequest))]
[JsonSerializable(typeof(RegisterUserRequest))]
[JsonSerializable(typeof(RegisterUserResponse))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(MfaLoginRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(SaasEngine.Api.Infrastructure.Outbox.UserRegisteredEvent))]
[JsonSerializable(typeof(System.Collections.Generic.List<string>))]
[JsonSerializable(typeof(SaasEngine.Contracts.Billing.FeatureFlagResponse))]
[JsonSerializable(typeof(System.Collections.Generic.List<SaasEngine.Contracts.Billing.FeatureFlagResponse>))]
[JsonSerializable(typeof(SaasEngine.Contracts.Billing.UpdateFeatureFlagRequest))]
[JsonSerializable(typeof(SaasEngine.Contracts.Billing.PlanResponse))]
[JsonSerializable(typeof(SaasEngine.Domain.Identity.User))]
[JsonSerializable(typeof(SaasEngine.Domain.Tenancy.Tenant))]
[JsonSerializable(typeof(SaasEngine.Domain.Billing.Plan))]
[JsonSerializable(typeof(SaasEngine.Domain.Billing.FeatureFlag))]
[JsonSerializable(typeof(SaasEngine.Domain.Audit.AuditEvent))]
[JsonSerializable(typeof(System.Collections.Generic.List<SaasEngine.Domain.Identity.User>))]
[JsonSerializable(typeof(System.Collections.Generic.List<SaasEngine.Domain.Billing.FeatureFlag>))]
[JsonSerializable(typeof(System.Collections.Generic.List<SaasEngine.Domain.Audit.AuditEvent>))]
[JsonSerializable(typeof(SaasEngine.Api.Infrastructure.BackgroundJobs.EraseUserDataPayload))]
[JsonSerializable(typeof(SaasEngine.Api.Infrastructure.BackgroundJobs.ExportTenantDataPayload))]
[JsonSerializable(typeof(SaasEngine.Contracts.Audit.AuditEventResponse))]
[JsonSerializable(typeof(System.Collections.Generic.List<SaasEngine.Contracts.Audit.AuditEventResponse>))]
[JsonSerializable(typeof(SaasEngine.Contracts.Audit.PagedResult<SaasEngine.Contracts.Audit.AuditEventResponse>))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
