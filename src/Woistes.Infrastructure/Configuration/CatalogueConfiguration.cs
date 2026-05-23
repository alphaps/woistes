using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Woistes.Domain;

namespace Woistes.Infrastructure.Configuration;

public class CatalogueConfiguration : IEntityTypeConfiguration<Catalogue>
{
    public void Configure(EntityTypeBuilder<Catalogue> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.Property(c => c.SourceFileName).HasMaxLength(512).IsRequired();

        builder.HasMany(c => c.Disks)
            .WithOne()
            .HasForeignKey(d => d.CatalogueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
