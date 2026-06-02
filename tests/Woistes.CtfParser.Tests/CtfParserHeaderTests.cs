using Woistes.CtfParser;
using Woistes.Domain;

namespace Woistes.CtfParser.Tests;

public class CtfParserHeaderTests
{
    private static string? FindSampleCtfDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "sampleCTF");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // Returns null when sample CTF files are unavailable (e.g. CI), so tests
    // can early-return as a no-op rather than fail.
    private Catalogue? ParseFile(string filename)
    {
        var dir = FindSampleCtfDir();
        if (dir == null) return null;
        using var stream = File.OpenRead(Path.Combine(dir, filename));
        var parser = new CtfFileParser();
        return parser.Parse(stream, filename);
    }

    [Fact]
    public void Parse_Boumbo40_ReadsCatalogueName()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        if (catalogue == null) return;
        Assert.Equal("Boumbo40", catalogue.Name);
    }

    [Fact]
    public void Parse_Boumbo40_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        if (catalogue == null) return;
        Assert.Equal(4, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskHasVolumeLabel()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        if (catalogue == null) return;
        var disk = catalogue.Disks[0];
        Assert.Equal("Kingston", disk.VolumeLabel);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskHasFilesystem()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        if (catalogue == null) return;
        var disk = catalogue.Disks[0];
        Assert.Equal("FAT", disk.FilesystemType);
    }

    [Fact]
    public void Parse_120Go_ReadsCatalogueName()
    {
        var catalogue = ParseFile("120 Go.CTF");
        if (catalogue == null) return;
        Assert.Equal("120 Go", catalogue.Name);
    }

    [Fact]
    public void Parse_120Go_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("120 Go.CTF");
        if (catalogue == null) return;
        Assert.Equal(8, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_120Go_FirstDiskHasVolumeLabel()
    {
        var catalogue = ParseFile("120 Go.CTF");
        if (catalogue == null) return;
        var disk = catalogue.Disks[0];
        Assert.Equal("Rack fat32", disk.VolumeLabel);
    }

    [Fact]
    public void Parse_120Go_FirstDiskHasFilesystem()
    {
        var catalogue = ParseFile("120 Go.CTF");
        if (catalogue == null) return;
        var disk = catalogue.Disks[0];
        Assert.Equal("FAT32", disk.FilesystemType);
    }

    [Fact]
    public void Parse_MyPassport_ReadsCatalogueName()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        if (catalogue == null) return;
        Assert.Equal("mypassport", catalogue.Name);
    }

    [Fact]
    public void Parse_MyPassport_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        if (catalogue == null) return;
        Assert.Equal(4, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_MyPassport_FirstDiskHasNtfs()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        if (catalogue == null) return;
        var disk = catalogue.Disks[0];
        Assert.Equal("NTFS", disk.FilesystemType);
    }

    [Fact]
    public void Parse_MesCd1_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Mes CD 1.CTF");
        if (catalogue == null) return;
        Assert.Equal(111, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_MesCd1_ReadsCatalogueName()
    {
        var catalogue = ParseFile("Mes CD 1.CTF");
        if (catalogue == null) return;
        Assert.Equal("Mes CD", catalogue.Name);
    }

    [Fact]
    public void Parse_MesCd2_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Mes CD 2.CTF");
        if (catalogue == null) return;
        Assert.Equal(133, catalogue.Disks.Count);
    }
}
