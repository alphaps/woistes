using Woistes.Domain;
using Woistes.Infrastructure;

namespace Woistes.Api.Tests;

public class EntryDataReaderTests
{
    [Fact]
    public void Read_IteratesAllEntries()
    {
        var entries = new List<CatalogueEntry>
        {
            new() { Id = 1, DiskId = 10, ParentId = null, Name = "root.txt", FullPath = "root.txt", Size = 100 },
            new() { Id = 2, DiskId = 10, ParentId = 1, Name = "child.txt", FullPath = "dir/child.txt", Size = 200,
                    CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2021, 6, 15) },
        };

        using var reader = new EntryDataReader(entries);

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetValue(0));
        Assert.Equal(10, reader.GetValue(1));
        Assert.Equal(DBNull.Value, reader.GetValue(2));
        Assert.Equal("root.txt", reader.GetValue(3));
        Assert.Equal(false, reader.GetValue(4));
        Assert.Equal("root.txt", reader.GetValue(5));
        Assert.Equal(100L, reader.GetValue(6));
        Assert.Equal(DBNull.Value, reader.GetValue(7));
        Assert.Equal(DBNull.Value, reader.GetValue(8));

        Assert.True(reader.Read());
        Assert.Equal(2L, reader.GetValue(0));
        Assert.Equal(1L, reader.GetValue(2));
        Assert.Equal(new DateTime(2020, 1, 1), reader.GetValue(7));
        Assert.Equal(new DateTime(2021, 6, 15), reader.GetValue(8));

        Assert.False(reader.Read());
    }

    [Fact]
    public void FieldCount_Returns9()
    {
        using var reader = new EntryDataReader([]);
        Assert.Equal(9, reader.FieldCount);
    }

    [Fact]
    public void GetOrdinal_MapsColumnNames()
    {
        using var reader = new EntryDataReader([]);
        Assert.Equal(0, reader.GetOrdinal("Id"));
        Assert.Equal(1, reader.GetOrdinal("DiskId"));
        Assert.Equal(2, reader.GetOrdinal("ParentId"));
        Assert.Equal(3, reader.GetOrdinal("Name"));
        Assert.Equal(4, reader.GetOrdinal("IsDirectory"));
        Assert.Equal(5, reader.GetOrdinal("FullPath"));
        Assert.Equal(6, reader.GetOrdinal("Size"));
        Assert.Equal(7, reader.GetOrdinal("CreatedDate"));
        Assert.Equal(8, reader.GetOrdinal("ModifiedDate"));
    }

    [Fact]
    public void IsDBNull_TrueForNullableFields()
    {
        var entries = new List<CatalogueEntry>
        {
            new() { Id = 1, DiskId = 5, Name = "f", FullPath = "f" },
        };
        using var reader = new EntryDataReader(entries);
        reader.Read();

        Assert.True(reader.IsDBNull(2));   // ParentId
        Assert.True(reader.IsDBNull(7));   // CreatedDate
        Assert.True(reader.IsDBNull(8));   // ModifiedDate
        Assert.False(reader.IsDBNull(0));  // Id
        Assert.False(reader.IsDBNull(3));  // Name
    }
}
