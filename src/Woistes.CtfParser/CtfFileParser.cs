using System.Text;
using Woistes.Domain;

namespace Woistes.CtfParser;

/// <summary>
/// Parser for WhereIsIt "Catalog 3.xx" (.CTF) binary catalogue files.
///
/// Structure (reverse-engineered, verified against the WhereIsIt GUI):
/// each disk section contains, in order:
///   1. A flat list of FILE entries in pre-order tree order (root's files first,
///      then folder-by-folder depth-first).
///   2. A block of DIRECTORY records, also in pre-order, each carrying a depth
///      and a direct-file count (f3). The tree is rebuilt by walking the dir
///      records with a depth stack and handing each folder its next f3 files
///      from the flat list. Files left over before the first folder are root.
/// </summary>
public class CtfFileParser : ICtfParser
{
    private const string MagicPrefix = "Catalog 3.";

    // File-entry markers and their metadata sizes (bytes after the name).
    private const ushort MarkerFull = 0x001C;    // 16 bytes: mod/cre/acc DOS date-time + size
    private const ushort MarkerShort = 0x0058;   // 12 bytes: mod/acc DOS date-time + size
    private const ushort MarkerAttr = 0x000C;    // 17 bytes: attr byte + full date-times + size
    private const ushort MarkerFull2 = 0x002C;   // 16 bytes: same layout as MarkerFull
    private const ushort MarkerGid = 0x0048;     // 13 bytes
    private const ushort MarkerFullExt = 0x041C; // 18 bytes: like MarkerFull + 2 extra bytes before size (rare)

    // Directory-record type byte (the 3rd byte of the 02 00 TT 00 marker) mapped
    // to its payload length (bytes after the name). Four variants are known.
    private static readonly Dictionary<byte, int> DirPayloadLengths = new()
    {
        [0x0C] = 40,
        [0x18] = 36,
        [0x2C] = 41,
        [0x38] = 37,
    };

    private const uint Sentinel = 0xFFFFFFFF;

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

        // Primary: walk the disk-descriptor length chain (robust, gives exact
        // boundaries, labels and root counts). Fall back to the legacy FS-string
        // scan only if the chain doesn't validate (doesn't end at EOF).
        var diskHeaders = ReadDiskDescriptors(data, pos, header.DiskCount)
            ?? FindAllDiskHeaders(data);

        for (int diskIdx = 0; diskIdx < header.DiskCount && diskIdx < diskHeaders.Count; diskIdx++)
        {
            var dh = diskHeaders[diskIdx];
            var sectionEnd = diskIdx + 1 < diskHeaders.Count
                ? diskHeaders[diskIdx + 1].Offset
                : data.Length;

            var disk = new Disk
            {
                DiskIndex = diskIdx,
                VolumeLabel = dh.VolumeLabel,
                FilesystemType = dh.FilesystemType,
            };

            ParseDiskSection(data, dh.DataStart, sectionEnd, disk);
            catalogue.Disks.Add(disk);
        }

        catalogue.FileCount = catalogue.Disks.SelectMany(d => Flatten(d.Entries)).Count(e => !e.IsDirectory);
        catalogue.FolderCount = catalogue.Disks.SelectMany(d => Flatten(d.Entries)).Count(e => e.IsDirectory);

        return catalogue;
    }

    private static void ParseDiskSection(byte[] data, int searchStart, int sectionEnd, Disk disk)
    {
        // The directory block is the contiguous run of valid dir records near the
        // end of the section. File entries occupy everything before it.
        var dirBlockStart = FindDirectoryBlockStart(data, searchStart, sectionEnd);
        var fileRegionEnd = dirBlockStart >= 0 ? dirBlockStart : sectionEnd;

        var files = ReadFileEntries(data, searchStart, fileRegionEnd);
        var dirs = dirBlockStart >= 0
            ? ReadDirectoryRecords(data, dirBlockStart, sectionEnd)
            : new List<DirRecord>();

        BuildTree(disk, files, dirs);
    }

    // ---- File entries ------------------------------------------------------

    private static List<CatalogueEntry> ReadFileEntries(byte[] data, int searchStart, int regionEnd)
    {
        var files = new List<CatalogueEntry>();
        var pos = FindFirstFileEntry(data, searchStart, regionEnd);
        if (pos < 0)
            return files;

        while (pos < regionEnd - 3)
        {
            var marker = PeekUInt16(data, pos);
            if ((marker == MarkerFull || marker == MarkerFull2) && IsValidNameStart(data, pos))
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
            else if (marker == MarkerGid && IsValidNameStart(data, pos))
            {
                pos += 2;
                files.Add(ReadFileEntryGid(data, ref pos));
            }
            else if (marker == MarkerFullExt && IsValidNameStart(data, pos))
            {
                pos += 2;
                files.Add(ReadFileEntryFullExt(data, ref pos));
            }
            else
            {
                var resyncPos = TryResync(data, pos + 1, regionEnd);
                if (resyncPos >= 0 && resyncPos < regionEnd)
                    pos = resyncPos;
                else
                    break;
            }
        }

        return files;
    }

    // ---- Directory records -------------------------------------------------

    private static List<DirRecord> ReadDirectoryRecords(byte[] data, int blockStart, int sectionEnd)
    {
        var dirs = new List<DirRecord>();
        var pos = blockStart;

        while (pos < sectionEnd - 7)
        {
            if (TryReadDirRecord(data, pos, out var dir, out var next))
            {
                dirs.Add(dir);
                pos = next;
                continue;
            }
            pos++;
        }

        return dirs;
    }

    /// <summary>
    /// Reads and validates a directory record at <paramref name="pos"/>.
    /// Layout: [02 00][type:1][00][depth:1][nameLen:2 LE][name][payload].
    /// The payload length depends on the type byte (see <see cref="DirPayloadLengths"/>);
    /// the direct-file count lives at payload offset +12.
    /// </summary>
    private static bool TryReadDirRecord(byte[] data, int pos, out DirRecord dir, out int next)
    {
        dir = default!;
        next = -1;

        if (pos + 7 > data.Length) return false;
        if (data[pos] != 0x02 || data[pos + 1] != 0x00 || data[pos + 3] != 0x00) return false;
        if (!DirPayloadLengths.TryGetValue(data[pos + 2], out int payloadLen)) return false;

        int depth = data[pos + 4];
        if (depth < 1 || depth > 30) return false;

        int nameLen = data[pos + 5] | (data[pos + 6] << 8);
        if (nameLen < 1 || nameLen > 260) return false;

        int nameStart = pos + 7;
        if (nameStart + nameLen + payloadLen > data.Length) return false;

        // Name must be printable — rejects false-positive markers in file data.
        for (int k = 0; k < nameLen; k++)
            if (data[nameStart + k] < 0x20) return false;

        var name = Encoding.Default.GetString(data, nameStart, nameLen);
        int payload = nameStart + nameLen;
        uint directFileCount = ReadUInt32At(data, payload + 12);

        dir = new DirRecord(name, depth, directFileCount == Sentinel ? 0 : (int)directFileCount);
        next = payload + payloadLen;
        return true;
    }

    /// <summary>
    /// Finds where the directory block begins: the first offset from which at
    /// least two directory records chain consecutively. File-entry data can
    /// coincidentally contain the marker bytes, but such false positives never
    /// form a valid chain.
    /// </summary>
    private static int FindDirectoryBlockStart(byte[] data, int from, int sectionEnd)
    {
        for (int i = from; i < sectionEnd - 7; i++)
        {
            if (TryReadDirRecord(data, i, out _, out var next)
                && next < sectionEnd
                && TryReadDirRecord(data, next, out _, out _))
            {
                return i;
            }
        }
        return -1;
    }

    // ---- Tree reconstruction ----------------------------------------------

    /// <summary>
    /// Rebuilds the folder tree from the flat pre-order file list and the
    /// pre-order directory records. Walks dirs with a depth stack; each folder
    /// consumes its next <see cref="DirRecord.DirectFileCount"/> files from the
    /// flat list. Files not consumed by any folder (the leading run) are root.
    /// </summary>
    private static void BuildTree(Disk disk, List<CatalogueEntry> files, List<DirRecord> dirs)
    {
        // Root's direct files = those before any folder's files = total - sum(f3).
        int consumedByDirs = dirs.Sum(d => d.DirectFileCount);
        int rootFileCount = Math.Max(0, files.Count - consumedByDirs);
        rootFileCount = Math.Min(rootFileCount, files.Count);

        int cursor = 0;
        for (int i = 0; i < rootFileCount; i++)
        {
            var f = files[cursor++];
            f.FullPath = f.Name;
            disk.Entries.Add(f);
        }

        // depth -> the folder entry currently open at that depth.
        var stack = new Stack<(CatalogueEntry Entry, int Depth)>();

        foreach (var dir in dirs)
        {
            var dirEntry = new CatalogueEntry { Name = dir.Name, IsDirectory = true };

            while (stack.Count > 0 && stack.Peek().Depth >= dir.Depth)
                stack.Pop();

            if (stack.Count > 0)
            {
                dirEntry.FullPath = $"{stack.Peek().Entry.FullPath}/{dir.Name}";
                stack.Peek().Entry.Children.Add(dirEntry);
            }
            else
            {
                dirEntry.FullPath = dir.Name;
                disk.Entries.Add(dirEntry);
            }

            int take = Math.Min(dir.DirectFileCount, files.Count - cursor);
            for (int i = 0; i < take; i++)
            {
                var f = files[cursor++];
                f.FullPath = $"{dirEntry.FullPath}/{f.Name}";
                dirEntry.Children.Add(f);
            }

            stack.Push((dirEntry, dir.Depth));
        }
    }

    // ---- Low-level file-entry readers -------------------------------------

    private static int FindFirstFileEntry(byte[] data, int searchStart, int searchEnd)
    {
        var limit = Math.Min(searchStart + 500, searchEnd - 3);
        for (int i = searchStart; i < limit; i++)
        {
            if (!IsFileMarker(data, i)) continue;

            var nextPos = SkipOneEntry(data, i);
            if (nextPos < 0 || nextPos >= searchEnd - 3) continue;
            if (IsFileMarker(data, nextPos))
                return i;
        }
        return -1;
    }

    private static int TryResync(byte[] data, int from, int limit)
    {
        var searchEnd = Math.Min(from + 256, limit - 3);
        for (int i = from; i < searchEnd; i++)
        {
            if (IsFileMarker(data, i))
            {
                var nextPos = SkipOneEntry(data, i);
                if (nextPos > 0 && nextPos < limit - 3 && IsFileMarker(data, nextPos))
                    return i;
            }
        }
        return -1;
    }

    private static int SkipOneEntry(byte[] data, int pos)
    {
        if (pos + 3 >= data.Length) return -1;
        var marker = PeekUInt16(data, pos);
        var nameLen = data[pos + 2];
        int metaSize = MetaSize(marker);
        if (metaSize < 0) return -1;
        return pos + 3 + nameLen + metaSize;
    }

    private static int MetaSize(ushort marker) => marker switch
    {
        MarkerFull => 16,
        MarkerFull2 => 16,
        MarkerShort => 12,
        MarkerAttr => 17,
        MarkerGid => 13,
        MarkerFullExt => 18,
        _ => -1,
    };

    private static bool IsFileMarker(byte[] data, int pos)
    {
        if (pos + 3 >= data.Length) return false;
        return MetaSize(PeekUInt16(data, pos)) >= 0 && IsValidNameStart(data, pos);
    }

    private static bool IsValidNameStart(byte[] data, int pos)
    {
        return pos + 3 < data.Length
            && data[pos + 2] >= 1
            && data[pos + 2] <= 250
            && data[pos + 3] >= 0x20;
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

        pos++; // attribute byte
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

    // Rare 13-byte metadata variant. Layout isn't fully reverse-engineered; we
    // read the leading DOS date-time and the trailing 4-byte size, which are the
    // fields the UI needs. Creation date is left unknown.
    private static CatalogueEntry ReadFileEntryGid(byte[] data, ref int pos)
    {
        var nameLen = data[pos++];
        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        var modTime = ReadUInt16(data, ref pos);
        var modDate = ReadUInt16(data, ref pos);
        pos += 5; // unidentified bytes
        var size = ReadUInt32(data, ref pos);

        return new CatalogueEntry
        {
            Name = name,
            IsDirectory = false,
            Size = size,
            ModifiedDate = DosDateTimeToDateTime(modDate, modTime),
        };
    }

    // Rare 18-byte metadata variant (marker 0x041C): like the 16-byte Full record
    // (mod/cre/acc DOS date-time) but with 2 extra bytes before the 4-byte size.
    private static CatalogueEntry ReadFileEntryFullExt(byte[] data, ref int pos)
    {
        var nameLen = data[pos++];
        var name = Encoding.Default.GetString(data, pos, nameLen);
        pos += nameLen;

        var modTime = ReadUInt16(data, ref pos);
        var modDate = ReadUInt16(data, ref pos);
        var creTime = ReadUInt16(data, ref pos);
        var creDate = ReadUInt16(data, ref pos);
        ReadUInt16(data, ref pos);  // accTime
        ReadUInt16(data, ref pos);  // accDate
        pos += 2;                   // 2 unidentified bytes
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

    // ---- Header & disk-header scanning ------------------------------------

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

    // Disk descriptors form a length chain immediately after the catalogue header
    // (8 zero pad bytes, then one descriptor per disk). Each descriptor:
    //   +0  u8  marker low byte (0x03 real disk, 0x13 CD, 0x83 virtual root folder)
    //   +1  u8  marker high byte (0x00)
    //   +8  u32 section length (descriptor + all content) — drives the chain
    //   +24 u32 root file count
    //   +32 u16 label length, +34 label bytes
    // Returns null if the chain doesn't validate (so the caller falls back to the
    // legacy FS-string scan for any format this doesn't fit).
    private static List<DiskHeaderInfo>? ReadDiskDescriptors(byte[] data, int afterHeaderPos, int diskCount)
    {
        const int padBytes = 8;
        var result = new List<DiskHeaderInfo>(diskCount);
        int p = afterHeaderPos + padBytes;

        for (int i = 0; i < diskCount; i++)
        {
            if (p + 36 > data.Length) return null;
            if (data[p + 1] != 0x00) return null;                 // marker high byte
            uint sectionLen = ReadUInt32At(data, p + 8);
            if (sectionLen == 0 || (long)p + sectionLen > data.Length) return null;

            int labelLen = data[p + 32] | (data[p + 33] << 8);
            if (labelLen < 1 || labelLen > 200 || p + 34 + labelLen > data.Length) return null;
            var label = Encoding.Default.GetString(data, p + 34, labelLen);

            // DataStart: just past the label; the file-entry scan starts here and
            // skips forward to the first real entry (past the inline FS node).
            int dataStart = p + 34 + labelLen;
            var fs = ReadFilesystemName(data, dataStart, dataStart + 200);

            result.Add(new DiskHeaderInfo(p, fs, label, dataStart));
            p += (int)sectionLen;
        }

        // The chain must consume the file exactly; otherwise it isn't this format.
        return p == data.Length ? result : null;
    }

    // Reads the first [len][NAME] filesystem string in [from, end), case-insensitively.
    private static string ReadFilesystemName(byte[] data, int from, int end)
    {
        var fsPatterns = new[] { "FAT32", "exFAT", "FAT", "NTFS", "CDFS", "UDF" };
        end = Math.Min(end, data.Length);
        for (int i = from; i < end; i++)
        {
            foreach (var fs in fsPatterns)
            {
                if (data[i] != fs.Length || i + 1 + fs.Length > data.Length) continue;
                var candidate = Encoding.ASCII.GetString(data, i + 1, fs.Length);
                if (string.Equals(candidate, fs, StringComparison.OrdinalIgnoreCase))
                    return fs;
            }
        }
        return "";
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

    // ---- Byte helpers ------------------------------------------------------

    private static ushort PeekUInt16(byte[] data, int pos)
        => (ushort)(data[pos] | (data[pos + 1] << 8));

    private static ushort ReadUInt16(byte[] data, ref int pos)
    {
        var val = (ushort)(data[pos] | (data[pos + 1] << 8));
        pos += 2;
        return val;
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        var val = ReadUInt32At(data, pos);
        pos += 4;
        return val;
    }

    private static uint ReadUInt32At(byte[] data, int pos)
        => (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));

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
    private record DirRecord(string Name, int Depth, int DirectFileCount);
}
