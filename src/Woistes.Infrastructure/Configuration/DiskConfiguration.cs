using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Woistes.Domain;

namespace Woistes.Infrastructure.Configuration;

public class DiskConfiguration : IEntityTypeConfiguration<Disk>
{
    public void Configure(EntityTypeBuilder<Disk> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.VolumeLabel).HasMaxLength(256);
        builder.Property(d => d.FilesystemType).HasMaxLength(16);

        builder.HasMany(d => d.Entries)
            .WithOne()
            .HasForeignKey(e => e.DiskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
