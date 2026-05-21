using FluentValidation;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Redis.StackExchange;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using Scalar.AspNetCore;
using SaasEngine.Api.Features.Audit;
using SaasEngine.Api.Features.Audit.Endpoints;
using SaasEngine.Api.Features.Billing;
using SaasEngine.Api.Features.Identity;
using SaasEngine.Api.Features.Identity.Endpoints;
using SaasEngine.Api.Features.Tenancy;
using SaasEngine.Api.Features.Tenancy.Endpoints;
using SaasEngine.Api.Features.Tenancy.Validators;
using SaasEngine.Api.Infrastructure.BackgroundJobs;
using SaasEngine.Api.Infrastructure.Data;
using SaasEngine.Api.Infrastructure.Messaging;
using SaasEngine.Api.Infrastructure.Outbox;
using SaasEngine.Api.Infrastructure.Redis;
using SaasEngine.Api.Infrastructure.Security;
using SaasEngine.Api.Infrastructure.Serialization;
using SaasEngine.Api.Infrastructure.Vault;
using SaasEngine.Api.Observability;
using SaasEngine.Contracts.Billing;
using SaasEngine.Contracts.Identity;
using SaasEngine.Contracts.Tenancy;
using SaasEngine.Domain.Shared;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ─── Dapper Type Handlers ──────
DapperTypeHandlers.Register();

// ─── Observability ───────────────────────────────────────────────
builder.AddSerilogLogging();
builder.AddOpenTelemetryObservability();

// ─── Health Checks ───────────────────────────────────────────────
builder.Services.AddHealthCheckServices(builder.Configuration);

// ─── MediatR (CQRS) ──────────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(PlanEnforcementBehavior<,>));
    cfg.AddOpenBehavior(typeof(AuditBehavior<,>));
});

// ─── FluentValidation ────────
builder.Services.AddScoped<IValidator<CreateTenantRequest>, CreateTenantValidator>();

// ─── HttpContext ─────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ─── Redis ───────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,password=DevRedis123!";
builder.Services.AddSingleton(new RedisConnectionProvider(redisConnectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    try { return sp.GetRequiredService<RedisConnectionProvider>().GetMultiplexer(); }
    catch { return null!; }
});

// ─── Secrets ─────────────────────────────────────────────────────
builder.Services.AddSingleton<ISecretStore, InMemorySecretStore>();

// ─── Database ────────────────────────────────────────────────────
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=saasengine;Username=saas_admin;Password=DevPassword123!";
builder.Services.AddSingleton<IAdminConnectionFactory>(new AdminConnectionFactory(defaultConnectionString));
builder.Services.AddScoped<IDbConnectionFactory, DapperConnectionFactory>();

// ─── Tenancy ─────────────────────────────────────────────────────
builder.Services.AddSingleton<ITenantConnectionResolver, TenantConnectionResolver>();
builder.Services.AddScoped<ITenantService, HttpContextTenantService>();
builder.Services.AddScoped<TenantResolutionMiddleware>();

// ─── Plan Policy & Billing ───────────────────────────────────────
builder.Services.AddScoped<IPlanPolicyService, PlanPolicyService>();

// ─── Event Bus ───────────────────────────────────────────────────
builder.Services.AddScoped<IEventBus, MediatREventBus>();

// ─── Distributed Locking ──────────────────────────────────────────
builder.Services.AddSingleton<IDistributedLock, SaasEngine.Api.Infrastructure.Locks.RedisDistributedLock>();

// ─── Outbox & Background Jobs ─────────────────────────────────────
builder.Services.AddScoped<OutboxService>();
builder.Services.AddScoped<OutboxPollerJob>();
builder.Services.AddScoped<BackgroundTaskDispatcher>();
builder.Services.AddScoped<IBackgroundTaskHandler, EraseUserDataJob>();
builder.Services.AddScoped<IBackgroundTaskHandler, ExportTenantDataJob>();

// ─── Audit & GDPR ───────────────────────────────────────────────
var auditConnectionString = builder.Configuration.GetConnectionString("AuditConnection")
    ?? defaultConnectionString.Replace("Username=saas_admin", "Username=saas_audit_writer").Replace("saas_admin", "saas_audit_writer");
builder.Services.AddSingleton<IAuditConnectionFactory>(sp => new AuditConnectionFactory(auditConnectionString, sp.GetRequiredService<IAdminConnectionFactory>()));
builder.Services.AddScoped<IAuditService, AuditService>();

builder.Services.AddHangfire((sp, config) =>
{
    var redis = sp.GetService<IConnectionMultiplexer>();
    if (redis is not null && redis.IsConnected)
    {
        try
        {
            config.UseRedisStorage(redis, new RedisStorageOptions
            {
                Prefix = "hangfire:",
                Db = 0
            });
            Serilog.Log.Information("Hangfire configured to use Redis storage.");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to configure Hangfire Redis storage. Falling back to in-memory.");
            config.UseInMemoryStorage();
        }
    }
    else
    {
        Serilog.Log.Warning("Redis is not connected. Hangfire will use in-memory storage.");
        config.UseInMemoryStorage();
    }
});
builder.Services.AddHangfireServer();

// ─── Quartz Registration ─────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    var jobKey = JobKey.Create(nameof(OutboxPollerJob));
    q.AddJob<OutboxPollerJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("OutboxPollerTrigger")
        .WithCronSchedule("*/10 * * * * ?"));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// ─── Authentication ──────────────────────────────────────────────
var jwtTokenGenerator = new JwtTokenGenerator(
    builder.Configuration["Jwt:PrivateKeyPem"]!,
    builder.Configuration["Jwt:Issuer"] ?? "SaasEngine",
    builder.Configuration["Jwt:Audience"] ?? "SaasEngine.Api"
);
builder.Services.AddSingleton(jwtTokenGenerator);
builder.Services.AddSingleton<TokenBlacklistService>();

builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "SaasEngine",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "SaasEngine.Api",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtTokenGenerator.GetPublicKey(),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var blacklistService = context.HttpContext.RequestServices.GetRequiredService<SaasEngine.Api.Infrastructure.Security.TokenBlacklistService>();
                var jti = context.Principal?.FindFirst("jti")?.Value 
                    ?? context.Principal?.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Jti)?.Value;

                if (!string.IsNullOrEmpty(jti) && await blacklistService.IsBlacklistedAsync(jti).ConfigureAwait(false))
                {
                    context.Fail("Token has been revoked.");
                }
            }
        };
    });
builder.Services.AddAuthorization();

// ─── JSON Serialization (AOT) ────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = AppJsonSerializerContext.Default;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapGet("/", () => Results.Redirect("/scalar/v1"));
app.MapOpenApi();
app.MapScalarApiReference();
app.UseHangfireDashboard("/hangfire");

app.MapHealthCheckEndpoints();
app.MapTenantEndpoints();
app.MapAuthEndpoints();
app.MapBillingEndpoints();
app.MapAuditEndpoints();

app.MapPost("/jobs/outbox/process", async ([FromServices] OutboxPollerJob poller, CancellationToken ct) =>
{
    await poller.ProcessOutboxEventsAsync(ct).ConfigureAwait(false);
    return Results.Ok(new MessageResponse("Processed"));
});

app.Run();

namespace SaasEngine.Api { public partial class Program { } }