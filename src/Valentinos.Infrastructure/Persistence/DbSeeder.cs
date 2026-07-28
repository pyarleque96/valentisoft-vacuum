using Microsoft.EntityFrameworkCore;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        var existe = await db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == "mastercorp");
        if (!existe)
        {
            db.Tenants.Add(Tenant.Create("mastercorp", "MasterCorp"));
            await db.SaveChangesAsync();
        }
    }
}
