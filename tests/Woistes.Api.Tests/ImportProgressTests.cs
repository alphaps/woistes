using Microsoft.EntityFrameworkCore;
using Woistes.Domain;
using Woistes.Infrastructure;

namespace Woistes.Api.Tests;

public class ImportProgressTests
{
    private static WoistesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WoistesDbContext>()
            .UseInMemoryDatabase("prog_" + Guid.NewGuid()).Options);

    private static Catalogue SampleCatalogue() => new()
    {
        Name = "Test",
        SourceFileName = "t.ctf",
        ImportedDate = DateTime.UtcNow,
        Disks =
        [
            new Disk { DiskIndex = 0, VolumeLabel = "D0", Entries =
                [ new CatalogueEntry { Name = "a", IsDirectory = true, Children =
                    [ new CatalogueEntry { Name = "f1" }, new CatalogueEntry { Name = "f2" } ] } ] },
            new Disk { DiskIndex = 1, VolumeLabel = "D1", Entries =
                [ new CatalogueEntry { Name = "b" } ] },
        ],
    };

    [Fact]
    public async Task AddAsync_ReportsProgressPerDisk()
    {
        using var db = NewDb();
        var repo = new CatalogueRepository(db);
        var reports = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(p => reports.Add(p));

        await repo.AddAsync(SampleCatalogue(), progress);

        // Progress<T> marshals callbacks; give them a moment to drain.
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(2, last.DisksTotal);
        Assert.Equal(2, last.DisksSaved);
        Assert.Equal(4, last.EntriesTotal);   // a, f1, f2, b
        Assert.Equal(4, last.EntriesSaved);
    }

    [Fact]
    public async Task AddAsync_WithoutProgress_StillPersists()
    {
        using var db = NewDb();
        var repo = new CatalogueRepository(db);

        var saved = await repo.AddAsync(SampleCatalogue());

        Assert.True(saved.Id > 0);
        Assert.Equal(4, db.Entries.Count());
    }
}
