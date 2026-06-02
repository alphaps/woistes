using Woistes.CtfParser;
using Woistes.Domain;

namespace Woistes.CtfParser.Tests;

public class CtfParserEntryTests
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

    private Catalogue ParseFile(string filename)
    {
        var dir = FindSampleCtfDir();
        if (dir == null)
            throw Xunit.Sdk.SkipException.ForSkip("sampleCTF not found");
        using var stream = File.OpenRead(Path.Combine(dir!, filename));
        var parser = new CtfFileParser();
        return parser.Parse(stream, filename);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskHasFiles()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        Assert.NotEmpty(disk.Entries);
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskContainsKnownFile()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        var allEntries = FlattenEntries(disk.Entries);
        Assert.Contains(allEntries, e => e.Name == "IF FOUND SEND TO - SI TROUVE ENVOYEZ A.txt");
    }

    [Fact]
    public void Parse_Boumbo40_FirstDiskContainsKnownFiles()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        var allEntries = FlattenEntries(disk.Entries);
        Assert.Contains(allEntries, e => e.Name == "Tools.zip");
        Assert.Contains(allEntries, e => e.Name == "MigoUninstall.exe");
        Assert.Contains(allEntries, e => e.Name == "MigoReadMe.pdf");
        Assert.Contains(allEntries, e => e.Name == "SecureTraveler.exe");
    }

    [Fact]
    public void Parse_Boumbo40_FilesHaveNonZeroSize()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var disk = catalogue.Disks[0];
        var allEntries = FlattenEntries(disk.Entries);
        var toolsZip = allEntries.First(e => e.Name == "Tools.zip");
        Assert.True(toolsZip.Size > 0);
    }

    [Fact]
    public void Parse_Boumbo40_HasDirectories()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        Assert.Contains(allEntries, e => e.IsDirectory && e.Name == "docs_CPP");
    }

    [Fact]
    public void Parse_Boumbo40_DirectoriesContainChildren()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        var docsCpp = allEntries.First(e => e.IsDirectory && e.Name == "docs_CPP");
        Assert.NotEmpty(docsCpp.Children);
    }

    [Fact]
    public void Parse_Boumbo40_TotalEntriesMatchHeader()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        var files = allEntries.Count(e => !e.IsDirectory);
        var folders = allEntries.Count(e => e.IsDirectory);
        Assert.Equal(catalogue.FileCount, files);
        Assert.Equal(catalogue.FolderCount, folders);
    }

    [Fact]
    public void Parse_120Go_HasFiles()
    {
        var catalogue = ParseFile("120 Go.CTF");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        Assert.True(allEntries.Count() > 100);
    }

    [Fact]
    public void Parse_MyPassport_HasFiles()
    {
        var catalogue = ParseFile("mypassport1000.CTF");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        Assert.True(allEntries.Count() > 100);
    }

    [Fact]
    public void Parse_Boumbo40_EntriesHaveFullPath()
    {
        var catalogue = ParseFile("Boumbo40.ctf");
        var allEntries = FlattenEntries(catalogue.Disks.SelectMany(d => d.Entries));
        var docsCpp = allEntries.First(e => e.IsDirectory && e.Name == "docs_CPP");
        Assert.Contains("docs_CPP", docsCpp.FullPath);

        if (docsCpp.Children.Count > 0)
        {
            var child = docsCpp.Children.First();
            Assert.Contains("docs_CPP/", child.FullPath);
        }
    }

    private static IEnumerable<CatalogueEntry> FlattenEntries(IEnumerable<CatalogueEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var child in FlattenEntries(entry.Children))
                yield return child;
        }
    }
}
