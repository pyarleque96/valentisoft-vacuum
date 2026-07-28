using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Common;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            e.Property(t => t.Nombre).HasMaxLength(200).IsRequired();
        });

        // Global query filter por TenantId para toda entidad ITenantOwned
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var tenantIdProp = Expression.Property(param, nameof(ITenantOwned.TenantId));
                var currentTenant = Expression.Property(
                    Expression.Constant(this), nameof(CurrentTenantId));
                var body = Expression.Equal(tenantIdProp, currentTenant);
                var lambda = Expression.Lambda(body, param);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    // Usado por el query filter; Guid.Empty cuando no hay tenant (no matchea nada)
    public Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

    public override int SaveChanges()
    {
        StampTenantAndTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantAndTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTenantAndTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is BaseEntity be && entry.State == EntityState.Modified)
                be.UpdatedAt = DateTime.UtcNow;

            if (entry.Entity is not ITenantOwned owned)
                continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    // Toda entidad multi-tenant se sella con el tenant del contexto.
                    // Sin tenant en contexto no se permite escribir (fail-closed).
                    if (_tenantContext.TenantId is not Guid tid)
                        throw new InvalidOperationException(
                            "No hay tenant en contexto al insertar una entidad multi-tenant.");
                    // Un TenantId explícito distinto al del contexto es un intento de
                    // escribir para otro tenant: se rechaza.
                    if (owned.TenantId != Guid.Empty && owned.TenantId != tid)
                        throw new UnauthorizedAccessException(
                            "No se puede crear una entidad para otro tenant.");
                    owned.TenantId = tid;
                    break;

                case EntityState.Modified:
                    // El TenantId de una entidad existente es inmutable: reasignarlo
                    // movería la fila a otro tenant.
                    var original = (Guid)entry.OriginalValues[nameof(ITenantOwned.TenantId)]!;
                    if (owned.TenantId != original)
                        throw new UnauthorizedAccessException(
                            "No se puede cambiar el tenant de una entidad existente.");
                    // El registro modificado debe pertenecer al tenant del contexto.
                    if (_tenantContext.TenantId is Guid ctid && original != ctid)
                        throw new UnauthorizedAccessException(
                            "No se puede modificar una entidad de otro tenant.");
                    break;
            }
        }
    }
}
