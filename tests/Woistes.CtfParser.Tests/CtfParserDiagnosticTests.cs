using Woistes.CtfParser;
using Woistes.Domain;
using Xunit.Abstractions;

namespace Woistes.CtfParser.Tests;

public class CtfParserDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public CtfParserDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    [Fact]
    public void Diagnostic_Boumbo40_DiskSummary()
    {
        var dir = FindSampleCtfDir();
        if (dir == null) { throw Xunit.Sdk.SkipException.ForSkip("sampleCTF not found"); return; }
        using var stream = File.OpenRead(Path.Combine(dir, "Boumbo40.ctf"));
        var parser = new CtfFileParser();
        var cat = parser.Parse(stream, "Boumbo40.ctf");

        _output.WriteLine($"Catalogue: {cat.Name}, Disks: {cat.Disks.Count}, Files: {cat.FileCount}, Folders: {cat.FolderCount}");
        for (int i = 0; i < cat.Disks.Count; i++)
        {
            var disk = cat.Disks[i];
            var flatEntries = Flatten(disk.Entries).ToList();
            _output.WriteLine($"  Disk {i}: Label='{disk.VolumeLabel}', FS='{disk.FilesystemType}', Entries={flatEntries.Count}");
            foreach (var e in flatEntries.Take(10))
                _output.WriteLine($"    {(e.IsDirectory ? "[DIR]" : "[FIL]")} {e.FullPath} ({e.Size} bytes)");
            if (flatEntries.Count > 10)
                _output.WriteLine($"    ... and {flatEntries.Count - 10} more");
        }

        Assert.True(true);
    }

    private static IEnumerable<CatalogueEntry> Flatten(IEnumerable<CatalogueEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var child in Flatten(entry.Children))
                yield return child;
        }
    }
}
