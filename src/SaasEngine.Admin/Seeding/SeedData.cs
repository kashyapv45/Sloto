using Microsoft.EntityFrameworkCore;
using SaasEngine.Admin.Data;

namespace SaasEngine.Admin.Seeding;

/// <summary>
/// Seeds initial data into the database.
/// </summary>
public static class SeedData
{
    /// <summary>Seeds default plans into the database.</summary>
    public static async Task SeedAsync(AdminDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Set<PlanEntity>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return; // Already seeded
        }

        var plans = new[]
        {
            new PlanEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Key = "starter",
                MaxSeats = 5,
                MaxApiCallsPerMinute = 60,
                Features = "[]",
                PriceMonthlyCents = 0
            },
            new PlanEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Key = "growth",
                MaxSeats = 25,
                MaxApiCallsPerMinute = 300,
                Features = "[\"advanced-analytics\", \"custom-branding\"]",
                PriceMonthlyCents = 4900
            },
            new PlanEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Key = "enterprise",
                MaxSeats = 1000,
                MaxApiCallsPerMinute = 10000,
                Features = "[\"advanced-analytics\", \"custom-branding\", \"sso\", \"dedicated-db\", \"priority-support\"]",
                PriceMonthlyCents = 29900
            }
        };

        context.Set<PlanEntity>().AddRange(plans);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
