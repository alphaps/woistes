using Woistes.CtfParser;
using Woistes.Domain;

namespace Woistes.CtfParser.Tests;

public class CtfParserHeaderTests
{
    private static string SamplePath(string filename) =>
        Path.Combine(FindSampleCtfDir(), filename);

    private static string FindSampleCtfDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "sampleCTF");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Cannot find sampleCTF directory");
    }

    private Catalogue ParseFile(string filename)
    {
        using var stream = File.OpenRead(SamplePath(filename));
        var parser = new CtfFileParser();
        return parser.Parse(stream, filename);
    }

    [Fact]
    public void Parse_Boumbo40_ReadsCatalogueName()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        Assert.Equal("Boumbo40", catalogue.Name);
    }

    [Fact]
    public void Parse_Boumbo40_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        Assert.Equal(4, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskHasVolumeLabel()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        Assert.Equal("Kingston", disk.VolumeLabel);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskHasFilesystem()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        Assert.Equal("FAT", disk.FilesystemType);
    }

    [Fact]
    public void Parse_120Go_ReadsCatalogueName()
    {
        var catalogue = ParseFile("120 Go.CTF");
        Assert.Equal("120 Go", catalogue.Name);
    }

    [Fact]
    public void Parse_120Go_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("120 Go.CTF");
        Assert.Equal(8, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_120Go_FirstDiskHasVolumeLabel()
    {
        var catalogue = ParseFile("120 Go.CTF");
        var disk = catalogue.Disks[0];
        Assert.Equal("Rack fat32", disk.VolumeLabel);
    }

    [Fact]
    public void Parse_120Go_FirstDiskHasFilesystem()
    {
        var catalogue = ParseFile("120 Go.CTF");
        var disk = catalogue.Disks[0];
        Assert.Equal("FAT32", disk.FilesystemType);
    }

    [Fact]
    public void Parse_MyPassport_ReadsCatalogueName()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        Assert.Equal("mypassport", catalogue.Name);
    }

    [Fact]
    public void Parse_MyPassport_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        Assert.Equal(4, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_MyPassport_FirstDiskHasNtfs()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        var disk = catalogue.Disks[0];
        Assert.Equal("NTFS", disk.FilesystemType);
    }

    [Fact]
    public void Parse_MesCd1_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Mes CD 1.CTF");
        Assert.Equal(111, catalogue.Disks.Count);
    }

    [Fact]
    public void Parse_MesCd1_ReadsCatalogueName()
    {
        var catalogue = ParseFile("Mes CD 1.CTF");
        Assert.Equal("Mes CD", catalogue.Name);
    }

    [Fact]
    public void Parse_MesCd2_HasCorrectDiskCount()
    {
        var catalogue = ParseFile("Mes CD 2.CTF");
        Assert.Equal(133, catalogue.Disks.Count);
    }
}
