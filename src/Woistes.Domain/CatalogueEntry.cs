namespace Woistes.Domain;

public class CatalogueEntry
{
    public long Id { get; set; }
    public int DiskId { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public List<CatalogueEntry> Children { get; set; } = [];
}
