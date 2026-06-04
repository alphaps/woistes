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
        // Full label from the disk descriptor (the old FS-back-scan truncated it).
        Assert.Equal("KingstonCle1Go", disk.VolumeLabel);
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
        // 5 disks: tc_vol, T7, the pour_d virtual root folder, PersBert81,
        // disqueE_LD5QAY. The old FS-string scan only found 4 (it missed the
        // uppercase "EXFAT" disk); the descriptor chain finds all 5.
        Assert.Equal(5, catalogue.Disks.Count);
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
    public void Parse_MyPassport_AllDiskLabelsFromDescriptorChain()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        if (catalogue == null) return;
        var labels = catalogue.Disks.Select(d => d.VolumeLabel).ToList();
        Assert.Equal(
            new[] { "tc_vol", "T7", "pour_d---referenceFigee22Jan12", "PersBert81", "disqueE_LD5QAY" },
            labels);
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
