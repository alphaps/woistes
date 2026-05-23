namespace Woistes.Domain;

public class Disk
{
    public int Id { get; set; }
    public int CatalogueId { get; set; }
    public string VolumeLabel { get; set; } = string.Empty;
    public string FilesystemType { get; set; } = string.Empty;
    public uint SerialNumber { get; set; }
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public DateTime? OriginalScanDate { get; set; }
    public int DiskIndex { get; set; }
    public List<CatalogueEntry> Entries { get; set; } = [];
}
