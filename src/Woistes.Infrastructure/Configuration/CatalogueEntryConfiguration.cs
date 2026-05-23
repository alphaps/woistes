using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Woistes.Domain;

namespace Woistes.Infrastructure.Configuration;

public class CatalogueEntryConfiguration : IEntityTypeConfiguration<CatalogueEntry>
{
    public void Configure(EntityTypeBuilder<CatalogueEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(512).IsRequired();
        builder.Property(e => e.FullPath).HasMaxLength(2048).IsRequired();

        builder.HasMany(e => e.Children)
            .WithOne()
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.DiskId);
        builder.HasIndex(e => e.ParentId);
        builder.HasIndex(e => e.FullPath);
        builder.HasIndex(e => e.Name);
    }
}
