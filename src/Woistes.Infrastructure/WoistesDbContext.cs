using Microsoft.EntityFrameworkCore;
using Woistes.Domain;

namespace Woistes.Infrastructure;

public class WoistesDbContext : DbContext
{
    public WoistesDbContext(DbContextOptions<WoistesDbContext> options) : base(options) { }

    public DbSet<Catalogue> Catalogues => Set<Catalogue>();
    public DbSet<Disk> Disks => Set<Disk>();
    public DbSet<CatalogueEntry> Entries => Set<CatalogueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WoistesDbContext).Assembly);
    }
}
