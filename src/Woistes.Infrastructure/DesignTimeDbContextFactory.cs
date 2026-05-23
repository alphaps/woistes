using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Woistes.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WoistesDbContext>
{
    public WoistesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WoistesDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost;Database=Woistes;Trusted_Connection=True;TrustServerCertificate=True");
        return new WoistesDbContext(optionsBuilder.Options);
    }
}
