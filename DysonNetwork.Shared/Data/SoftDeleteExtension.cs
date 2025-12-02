using System.Reflection;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Data;

public static class SoftDeleteExtension
{
    public static void ApplySoftDeleteFilters(this ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes();
        foreach (var entityType in entityTypes)
        {
            if (!typeof(ModelBase).IsAssignableFrom(entityType.ClrType)) continue;

            // Skip derived types to avoid filter conflicts
            var clrType = entityType.ClrType;
            if (clrType.BaseType != typeof(ModelBase) && 
                typeof(ModelBase).IsAssignableFrom(clrType.BaseType))
            {
                continue; // Skip derived types
            }

            // Apply soft delete filter using cached reflection
            var method = typeof(SoftDeleteExtension)
                .GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder]);
        }
    }

    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : ModelBase
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.DeletedAt == null);
    }

    public static void ApplyAuditableAndSoftDelete(this DbContext context)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        foreach (var entry in context.ChangeTracker.Entries<ModelBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAt = now;
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }
    }
}
