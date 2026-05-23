using System.Text;
using Woistes.Domain;

namespace Woistes.CtfParser;

public class CtfFileParser : ICtfParser
{
    private const string MagicPrefix = "Catalog 3.";
    private const ushort MarkerFull = 0x001C;
    private const ushort MarkerShort = 0x0058;
    private const ushort MarkerAttr = 0x000C;

    public Catalogue Parse(Stream stream, string sourceFileName)
    {
        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        var pos = 0;
        var header = ReadHeader(data, ref pos);

        var catalogue = new Catalogue
        {
            Name = header.Name,
            SourceFileName = sourceFileName,
            ImportedDate = DateTime.UtcNow,
        };

        var diskHeaders = FindAllDiskHeaders(data);

        // Phase 1: Read all file entries and directory entries from the entire file
        var globalFiles = new List<CatalogueEntry>();
        var globalDirs = new List<DirRecord>();
        var diskFileBoundaries = new List<(int startIdx, int count)>();

        for (int diskIdx = 0; diskIdx < header.DiskCount && diskIdx < diskHeaders.Count; diskIdx++)
        {
            var dh = diskHeaders[diskIdx];
            var sectionEnd = diskIdx + 1 < diskHeaders.Count
                ? diskHeaders[diskIdx + 1].Offset
                : data.Length;

            var startIdx = globalFiles.Count;
            ReadDiskEntries(data, dh.DataStart, sectionEnd, globalFiles, globalDirs);
            diskFileBoundaries.Add((startIdx, globalFiles.Count - startIdx));
        }

        // Phase 2: Assign directories to disks and build trees
        for (int diskIdx = 0; diskIdx < header.DiskCount && diskIdx < diskHeaders.Count; diskIdx++)
        {
            var dh = diskHeaders[diskIdx];
            var (startIdx, fileCount) = diskFileBoundaries[diskIdx];

            var disk = new Disk
            {
                DiskIndex = diskIdx,
                VolumeLabel = dh.VolumeLabel,
                FilesystemType = dh.FilesystemType,
            };

            // Find directories that reference this disk's files
            var diskDirs = FindDirsForDisk(globalDirs, startIdx, startIdx + fileCount);

            // Root files: those before the first directory's start index
            int rootEnd = fileCount;
            foreach (var dir in diskDirs)
            {
                if (dir.StartIndex >= startIdx && dir.StartIndex < startIdx + fileCount)
                {
                    var localIdx = dir.StartIndex - startIdx;
                    if (localIdx < rootEnd)
                        rootEnd = localIdx;
                }
            }

            for (int i = startIdx; i < startIdx + rootEnd && i < globalFiles.Count; i++)
            {
                var f = globalFiles[i];
                f.FullPath = f.Name;
                disk.Entries.Add(f);
            }

            BuildDirectoryTree(disk, globalFiles, diskDirs);

            catalogue.Disks.Add(disk);
        }

        catalogue.FileCount = catalogue.Disks.SelectMany(d => Flatten(d.Entries)).Count(e => !e.IsDirectory);
        catalogue.FolderCount = catalogue.Disks.SelectMany(d => Flatten(d.Entries)).Count(e => e.IsDirectory);

        return catalogue;
    }

    private static void ReadDiskEntries(byte[] data, int searchStart, int sectionEnd,
        List<CatalogueEntry> files, List<DirRecord> dirs)
    {
        var pos = FindFirstFileEntry(data, searchStart, sectionEnd);
        if (pos < 0)
            return;

        while (pos < sectionEnd - 3)
        {
            if (IsDirMarker(data, pos))
            {
                while (pos < sectionEnd - 4 && IsDirMarker(data, pos))
                {
                    pos += 4;
                    var dir = ReadDirectoryRecord(data, ref pos);
                    if (dir != null)
                        dirs.Add(dir);
                    else
                        break;
                }
                // After dirs, try to find more file entries
                var resyncPos = TryResync(data, pos, sectionEnd);
                if (resyncPos >= 0)
                    pos = resyncPos;
                else
                    break;
                continue;
            }

            var marker = PeekUInt16(data, pos);
            if (marker == MarkerFull && IsValidNameStart(data, pos))
            {
                pos += 2;
                files.Add(ReadFileEntryFull(data, ref pos));
            }
            else if (marker == MarkerShort && IsValidNameStart(data, pos))
            {
                pos += 2;
                files.Add(ReadFileEntryShort(data, ref pos));
            }
            else if (marker == MarkerAttr && IsValidNameStart(data, pos))
            {
                pos += 2;
                files.Add(ReadFileEntryAttr(data, ref pos));
            }
            else
            {
                var resyncPos = TryResync(data, pos + 1, sectionEnd);
                if (resyncPos >= 0)
                    pos = resyncPos;
                else
                    break;
            }
        }
    }

    private static List<DirRecord> FindDirsForDisk(List<DirRecord> allDirs, int diskStart, int diskEnd)
    {
        // Find the contiguous block of directories whose start indices fall within this disk's range
        // Directories are in file order. We want dirs that reference files in [diskStart, diskEnd).
        // Also include dirs with startIndex=-1 (empty/virtual) that are nested under relevant dirs.
        var result = new List<DirRecord>();
        bool inRange = false;

        foreach (var dir in allDirs)
        {
            if (dir.StartIndex >= diskStart && dir.StartIndex < diskEnd)
            {
                inRange = true;
                result.Add(dir);
            }
            else if (dir.StartIndex == -1 && inRange)
            {
                result.Add(dir);
            }
            else if (inRange && dir.StartIndex >= 0)
            {
                if (dir.StartIndex >= diskEnd)
                    break;
            }
        }

        return result;
    }

    private static int TryResync(byte[] data, int from, int limit)
    {
        var searchEnd = Math.Min(from + 256, limit - 3);
        for (int i = from; i < searchEnd; i++)
        {
            if (IsDirMarker(data, i))
                return i;
            var marker = PeekUInt16(data, i);
            if ((marker == MarkerFull || marker == MarkerShort || marker == MarkerAttr)
                && IsValidNameStart(data, i))
            {
                var nextPos = SkipOneEntry(data, i);
                if (nextPos > 0 && nextPos < limit - 3)
                {
                    if (IsFileMarker(data, nextPos) || IsDirMarker(data, nextPos))
                        return i;
                }
            }
        }
        return -1;
    }

    private static void BuildDirectoryTree(Disk disk, List<CatalogueEntry> globalFiles, List<DirRecord> directories)
    {
        if (directories.Count == 0)
            return;

        var dirStack = new Stack<(CatalogueEntry Entry, int Depth)>();

        foreach (var dir in directories)
        {
            var dirEntry = new CatalogueEntry
            {
                Name = dir.Name,
                IsDirectory = true,
            };

            while (dirStack.Count > 0 && dirStack.Peek().Depth >= dir.Depth)
                dirStack.Pop();

            if (dirStack.Count > 0)
                dirEntry.FullPath = $"{dirStack.Peek().Entry.FullPath}/{dir.Name}";
            else
                dirEntry.FullPath = dir.Name;

            if (dir.StartIndex >= 0 && dir.FileCount > 0 && dir.StartIndex < globalFiles.Count)
            {
                var end = Math.Min(dir.StartIndex + dir.FileCount, globalFiles.Count);
                for (int i = dir.StartIndex; i < end; i++)
                {
                    var file = globalFiles[i];
                    file.FullPath = $"{dirEntry.FullPath}/{file.Name}";
                    dirEntry.Children.Add(file);
                }
            }

            if (dirStack.Count > 0)
                dirStack.Peek().Entry.Children.Add(dirEntry);
            else
                disk.Entries.Add(dirEntry);

            dirStack.Push((dirEntry, dir.Depth));
        }
    }

    private static int FindFirstFileEntry(byte[] data, int searchStart, int searchEnd)
    {
        var limit = Math.Min(searchStart + 500, searchEnd - 3);
        for (int i = searchStart; i < limit; i++)
        {
            if (!IsValidNameStart(data, i)) continue;
            var marker = PeekUInt16(data, i);
            if (marker != MarkerFull && marker != MarkerShort && marker != MarkerAttr)
                continue;

            var nextPos = SkipOneEntry(data, i);
            if (nextPos < 0 || nextPos >= searchEnd - 3) continue;
            if (IsFileMarker(data, nextPos) || IsDirMarker(data, nextPos))
                return i;
        }
        return -1;
    }

    private static int SkipOneEntry(byte[] data, int pos)
    {
        if (pos + 3 >= data.Length) return -1;
        var marker = PeekUInt16(data, pos);
        var nameLen = data[pos + 2];
        int metaSize = marker switch
        {
            MarkerFull => 16,
            MarkerShort => 12,
            MarkerAttr => 17,
            _ => -1,
        };
        if (metaSize < 0) return -1;
        return pos + 3 + nameLen + metaSize;
    }

    private static bool IsFileMarker(byte[] data, int pos)
    {
        if (pos + 3 >= data.Length) return false;
        var marker = PeekUInt16(data, pos);
        return (marker == MarkerFull || marker == MarkerShort || marker == MarkerAttr)
            && IsValidNameStart(data, pos);
    }

    private static bool IsValidNameStart(byte[] data, int pos)
    {
        return pos + 3 < data.Length
            && data[pos + 2] >= 1
            && data[pos + 2] <= 250
            && data[pos + 3] >= 0x20;
    }

    private static bool IsDirMarker(byte[] data, int pos)
    {
        if (pos + 4 >= data.Length) return false;
        return data[pos] == 0x02 && data[pos + 1] == 0x00
            && (data[pos + 2] == 0x0C || data[pos + 2] == 0x2C)
            && data[pos + 3] == 0x00;
    }

    private static CatalogueEntry ReadFileEntryFull(byte[] data, ref int pos)
    {
        var nameLen = data[pos++];
        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        var modTime = ReadUInt16(data, ref pos);
        var modDate = ReadUInt16(data, ref pos);
        var creTime = ReadUInt16(data, ref pos);
        var creDate = ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        var size = ReadUInt32(data, ref pos);

        return new CatalogueEntry
        {
            Name = name,
            IsDirectory = false,
            Size = size,
            ModifiedDate = DosDateTimeToDateTime(modDate, modTime),
            CreatedDate = DosDateTimeToDateTime(creDate, creTime),
        };
    }

    private static CatalogueEntry ReadFileEntryShort(byte[] data, ref int pos)
    {
        var nameLen = data[pos++];
        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        var modTime = ReadUInt16(data, ref pos);
        var modDate = ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        var size = ReadUInt32(data, ref pos);

        return new CatalogueEntry
        {
            Name = name,
            IsDirectory = false,
            Size = size,
            ModifiedDate = DosDateTimeToDateTime(modDate, modTime),
        };
    }

    private static CatalogueEntry ReadFileEntryAttr(byte[] data, ref int pos)
    {
        var nameLen = data[pos++];
        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        pos++;
        var modTime = ReadUInt16(data, ref pos);
        var modDate = ReadUInt16(data, ref pos);
        var creTime = ReadUInt16(data, ref pos);
        var creDate = ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);
        var size = ReadUInt32(data, ref pos);

        return new CatalogueEntry
        {
            Name = name,
            IsDirectory = false,
            Size = size,
            ModifiedDate = DosDateTimeToDateTime(modDate, modTime),
            CreatedDate = DosDateTimeToDateTime(creDate, creTime),
        };
    }

    private static DirRecord? ReadDirectoryRecord(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return null;

        var depth = data[pos++];
        if (depth < 1 || depth > 30) return null;

        if (pos + 2 > data.Length) return null;
        var nameLen = ReadUInt16(data, ref pos);
        if (nameLen == 0 || nameLen > 260 || pos + nameLen > data.Length) return null;

        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        if (pos + 4 > data.Length) return null;
        pos += 4;

        if (pos + 12 > data.Length) return null;
        var startIndex = (int)ReadUInt32(data, ref pos);
        var fileCount = (int)ReadUInt32(data, ref pos);
        var subdirCount = (int)ReadUInt32(data, ref pos);

        if (pos + 24 > data.Length) return null;
        pos += 24;

        if (startIndex == unchecked((int)0xFFFFFFFF))
            startIndex = -1;
        if (fileCount == unchecked((int)0xFFFFFFFF))
            fileCount = 0;

        return new DirRecord(name, depth, startIndex, fileCount, subdirCount);
    }

    private static CatalogueHeader ReadHeader(byte[] data, ref int pos)
    {
        var magic = Encoding.ASCII.GetString(data, 0, 12).TrimEnd('\0');
        if (!magic.StartsWith(MagicPrefix))
            throw new InvalidDataException($"Invalid CTF magic: '{magic}'");
        pos = 12;
        pos += 4;

        var diskCount = ReadUInt16(data, ref pos);
        var diskIds = new ushort[diskCount];
        for (int i = 0; i < diskCount; i++)
            diskIds[i] = ReadUInt16(data, ref pos);

        var nameLength = ReadUInt16(data, ref pos);
        var name = Encoding.Default.GetString(data, pos, nameLength);
        pos += nameLength;

        return new CatalogueHeader(name, diskCount, diskIds);
    }

    private static List<DiskHeaderInfo> FindAllDiskHeaders(byte[] data)
    {
        var fsPatterns = new[] { "FAT32", "exFAT", "FAT", "NTFS", "CDFS", "UDF" };
        var results = new List<DiskHeaderInfo>();

        for (int i = 0; i < data.Length - 10; i++)
        {
            foreach (var fs in fsPatterns)
            {
                if (i + 1 + fs.Length > data.Length) continue;
                if (data[i] != fs.Length) continue;
                var candidate = Encoding.ASCII.GetString(data, i + 1, fs.Length);
                if (candidate != fs) continue;

                string label = "";
                for (int back = 1; back < 40 && i - back >= 0; back++)
                {
                    if (data[i - back] == back - 1 && back - 1 > 0 && back - 1 < 40)
                    {
                        label = Encoding.Default.GetString(data, i - back + 1, back - 1);
                        break;
                    }
                }
                results.Add(new DiskHeaderInfo(i, fs, label, i + 1 + fs.Length));
            }
        }
        return results;
    }

    private static ushort PeekUInt16(byte[] data, int pos)
    {
        return (ushort)(data[pos] | (data[pos + 1] << 8));
    }

    private static ushort ReadUInt16(byte[] data, ref int pos)
    {
        var val = (ushort)(data[pos] | (data[pos + 1] << 8));
        pos += 2;
        return val;
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        var val = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
        pos += 4;
        return val;
    }

    private static DateTime? DosDateTimeToDateTime(ushort date, ushort time)
    {
        if (date == 0 && time == 0) return null;
        try
        {
            int year = 1980 + (date >> 9);
            int month = (date >> 5) & 0xF;
            int day = date & 0x1F;
            int hour = time >> 11;
            int minute = (time >> 5) & 0x3F;
            int second = (time & 0x1F) * 2;
            if (month < 1 || month > 12 || day < 1 || day > 31) return null;
            if (hour > 23 || minute > 59 || second > 59) return null;
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        }
        catch { return null; }
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

    private record CatalogueHeader(string Name, ushort DiskCount, ushort[] DiskIds);
    private record DiskHeaderInfo(int Offset, string FilesystemType, string VolumeLabel, int DataStart);
    private record DirRecord(string Name, int Depth, int StartIndex, int FileCount, int SubdirCount);
}
