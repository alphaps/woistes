using System.Data;
using Woistes.Domain;

namespace Woistes.Infrastructure;

internal sealed class EntryDataReader : IDataReader
{
    private static readonly string[] Columns =
        ["Id", "DiskId", "ParentId", "Name", "IsDirectory", "FullPath", "Size", "CreatedDate", "ModifiedDate"];

    private readonly List<CatalogueEntry> _entries;
    private int _index = -1;

    public EntryDataReader(List<CatalogueEntry> entries) => _entries = entries;

    private CatalogueEntry Current => _entries[_index];

    public int FieldCount => Columns.Length;

    public bool Read() => ++_index < _entries.Count;

    public object GetValue(int i) => i switch
    {
        0 => Current.Id,
        1 => Current.DiskId,
        2 => Current.ParentId.HasValue ? Current.ParentId.Value : DBNull.Value,
        3 => Current.Name,
        4 => Current.IsDirectory,
        5 => Current.FullPath,
        6 => Current.Size,
        7 => Current.CreatedDate.HasValue ? Current.CreatedDate.Value : DBNull.Value,
        8 => Current.ModifiedDate.HasValue ? Current.ModifiedDate.Value : DBNull.Value,
        _ => throw new IndexOutOfRangeException(),
    };

    public int GetOrdinal(string name) => Array.IndexOf(Columns, name);
    public string GetName(int i) => Columns[i];
    public Type GetFieldType(int i) => i switch
    {
        0 => typeof(long),
        1 => typeof(int),
        2 => typeof(long),
        3 => typeof(string),
        4 => typeof(bool),
        5 => typeof(string),
        6 => typeof(long),
        7 => typeof(DateTime),
        8 => typeof(DateTime),
        _ => typeof(object),
    };

    public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;
    public int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public void Dispose() { }
    public void Close() { }
    public bool NextResult() => false;
    public int Depth => 0;
    public bool IsClosed => false;
    public int RecordsAffected => -1;
    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => 0;
    public char GetChar(int i) => (char)GetValue(i);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => 0;
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => GetFieldType(i).Name;
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public DataTable GetSchemaTable() => throw new NotSupportedException();
}
