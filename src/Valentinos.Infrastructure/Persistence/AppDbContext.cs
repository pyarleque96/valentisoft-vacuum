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
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportPhoto> ReportPhotos => Set<ReportPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            e.Property(t => t.Nombre).HasMaxLength(200).IsRequired();
            e.Property(t => t.EmailNotificationsEnabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<AssetType>(e =>
        {
            e.Property(t => t.Nombre).HasMaxLength(200).IsRequired();
            e.Property(t => t.Prefijo).HasMaxLength(20).IsRequired();
            e.HasIndex(t => new { t.TenantId, t.Prefijo }).IsUnique();
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.Property(a => a.Codigo).HasMaxLength(40).IsRequired();
            e.Property(a => a.Ubicacion).HasMaxLength(200);
            e.HasIndex(a => new { a.TenantId, a.Codigo }).IsUnique();
            e.HasOne<AssetType>()
             .WithMany()
             .HasForeignKey(a => a.AssetTypeId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.Property(r => r.Descripcion).HasMaxLength(2000).IsRequired();
            e.Property(r => r.Ubicacion).HasMaxLength(200);
            e.Property(r => r.ReportadoPor).HasMaxLength(200);
            e.Property(r => r.Notas).HasMaxLength(2000);
            e.HasIndex(r => new { r.TenantId, r.AssetId });
            e.HasOne<Asset>()
             .WithMany()
             .HasForeignKey(r => r.AssetId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReportPhoto>(e =>
        {
            e.Property(p => p.FileKey).HasMaxLength(400).IsRequired();
            e.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            e.HasIndex(p => p.ReportId);
            e.HasOne<Report>()
             .WithMany()
             .HasForeignKey(p => p.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
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

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // Ninguna escritura (alta, modificación o borrado) sobre una entidad
            // multi-tenant se permite sin tenant en contexto (fail-closed).
            if (_tenantContext.TenantId is not Guid tid)
                throw new InvalidOperationException(
                    "No hay tenant en contexto al escribir una entidad multi-tenant.");

            switch (entry.State)
            {
                case EntityState.Added:
                    // Un TenantId explícito distinto al del contexto es un intento de
                    // escribir para otro tenant: se rechaza.
                    if (owned.TenantId != Guid.Empty && owned.TenantId != tid)
                        throw new UnauthorizedAccessException(
                            "No se puede crear una entidad para otro tenant.");
                    owned.TenantId = tid;
                    break;

                case EntityState.Modified:
                {
                    // El registro modificado debe pertenecer al tenant del contexto.
                    var original = (Guid)entry.OriginalValues[nameof(ITenantOwned.TenantId)]!;
                    if (original != tid)
                        throw new UnauthorizedAccessException(
                            "No se puede modificar una entidad de otro tenant.");
                    // El TenantId de una entidad existente es inmutable: reasignarlo
                    // movería la fila a otro tenant.
                    if (owned.TenantId != original)
                        throw new UnauthorizedAccessException(
                            "No se puede cambiar el tenant de una entidad existente.");
                    break;
                }

                case EntityState.Deleted:
                {
                    // Solo se puede borrar una fila que pertenezca al tenant del contexto.
                    var original = (Guid)entry.OriginalValues[nameof(ITenantOwned.TenantId)]!;
                    if (original != tid)
                        throw new UnauthorizedAccessException(
                            "No se puede borrar una entidad de otro tenant.");
                    break;
                }
            }
        }
    }
}
