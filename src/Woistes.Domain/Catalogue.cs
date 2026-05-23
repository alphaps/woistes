namespace Woistes.Domain;

public class Catalogue
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public DateTime ImportedDate { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public List<Disk> Disks { get; set; } = [];
}
