using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Woistes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToSequenceEntryIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server cannot ALTER a column to remove IDENTITY within a
            // single batch. Strategy: create sequence + shadow table without
            // IDENTITY, copy all data, drop the original, rename the shadow.
            // Everything in one Sql() block so it either all succeeds or none.
            migrationBuilder.Sql("""
                -- Create sequence (idempotent: uses dynamic SQL for DDL inside IF)
                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'EntryIdSequence')
                    EXEC(N'CREATE SEQUENCE [dbo].[EntryIdSequence] AS bigint START WITH 1 INCREMENT BY 1');

                -- Drop constraints referencing Entries
                ALTER TABLE [Entries] DROP CONSTRAINT [FK_Entries_Entries_ParentId];
                ALTER TABLE [Entries] DROP CONSTRAINT [FK_Entries_Disks_DiskId];

                -- Create shadow table (same schema, no IDENTITY, sequence default)
                CREATE TABLE [Entries_new] (
                    [Id] bigint NOT NULL CONSTRAINT [DF_EntriesNew_Id] DEFAULT (NEXT VALUE FOR [dbo].[EntryIdSequence]),
                    [DiskId] int NOT NULL,
                    [ParentId] bigint NULL,
                    [Name] nvarchar(512) NOT NULL,
                    [IsDirectory] bit NOT NULL,
                    [FullPath] nvarchar(2048) NOT NULL,
                    [Size] bigint NOT NULL,
                    [CreatedDate] datetime2 NULL,
                    [ModifiedDate] datetime2 NULL,
                    CONSTRAINT [PK_EntriesNew] PRIMARY KEY ([Id])
                );

                -- Copy data
                INSERT INTO [Entries_new] ([Id],[DiskId],[ParentId],[Name],[IsDirectory],[FullPath],[Size],[CreatedDate],[ModifiedDate])
                SELECT [Id],[DiskId],[ParentId],[Name],[IsDirectory],[FullPath],[Size],[CreatedDate],[ModifiedDate]
                FROM [Entries];

                -- Drop original
                DROP TABLE [Entries];

                -- Rename shadow to Entries
                EXEC sp_rename N'Entries_new', N'Entries';
                EXEC sp_rename N'PK_EntriesNew', N'PK_Entries', N'OBJECT';
                EXEC sp_rename N'DF_EntriesNew_Id', N'DF_Entries_Id', N'OBJECT';

                -- Recreate indexes
                CREATE INDEX [IX_Entries_DiskId] ON [Entries] ([DiskId]);
                CREATE INDEX [IX_Entries_FullPath] ON [Entries] ([FullPath]);
                CREATE INDEX [IX_Entries_Name] ON [Entries] ([Name]);
                CREATE INDEX [IX_Entries_ParentId] ON [Entries] ([ParentId]);

                -- Recreate FKs
                ALTER TABLE [Entries] ADD CONSTRAINT [FK_Entries_Disks_DiskId]
                    FOREIGN KEY ([DiskId]) REFERENCES [Disks]([Id]) ON DELETE CASCADE;
                ALTER TABLE [Entries] ADD CONSTRAINT [FK_Entries_Entries_ParentId]
                    FOREIGN KEY ([ParentId]) REFERENCES [Entries]([Id]);

                -- Advance sequence past existing data
                DECLARE @maxId bigint = (SELECT ISNULL(MAX(Id), 0) FROM Entries);
                IF @maxId > 0
                BEGIN
                    DECLARE @sql nvarchar(200) = N'ALTER SEQUENCE [dbo].[EntryIdSequence] RESTART WITH ' + CAST(@maxId + 1 AS nvarchar(20));
                    EXEC sp_executesql @sql;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [Entries] DROP CONSTRAINT [FK_Entries_Entries_ParentId];
                ALTER TABLE [Entries] DROP CONSTRAINT [FK_Entries_Disks_DiskId];

                CREATE TABLE [Entries_old] (
                    [Id] bigint IDENTITY(1,1) NOT NULL,
                    [DiskId] int NOT NULL,
                    [ParentId] bigint NULL,
                    [Name] nvarchar(512) NOT NULL,
                    [IsDirectory] bit NOT NULL,
                    [FullPath] nvarchar(2048) NOT NULL,
                    [Size] bigint NOT NULL,
                    [CreatedDate] datetime2 NULL,
                    [ModifiedDate] datetime2 NULL,
                    CONSTRAINT [PK_Entries] PRIMARY KEY ([Id])
                );

                SET IDENTITY_INSERT [Entries_old] ON;
                INSERT INTO [Entries_old] ([Id],[DiskId],[ParentId],[Name],[IsDirectory],[FullPath],[Size],[CreatedDate],[ModifiedDate])
                SELECT [Id],[DiskId],[ParentId],[Name],[IsDirectory],[FullPath],[Size],[CreatedDate],[ModifiedDate]
                FROM [Entries];
                SET IDENTITY_INSERT [Entries_old] OFF;

                DROP TABLE [Entries];
                EXEC sp_rename N'Entries_old', N'Entries';

                CREATE INDEX [IX_Entries_DiskId] ON [Entries] ([DiskId]);
                CREATE INDEX [IX_Entries_FullPath] ON [Entries] ([FullPath]);
                CREATE INDEX [IX_Entries_Name] ON [Entries] ([Name]);
                CREATE INDEX [IX_Entries_ParentId] ON [Entries] ([ParentId]);

                ALTER TABLE [Entries] ADD CONSTRAINT [FK_Entries_Disks_DiskId]
                    FOREIGN KEY ([DiskId]) REFERENCES [Disks]([Id]) ON DELETE CASCADE;
                ALTER TABLE [Entries] ADD CONSTRAINT [FK_Entries_Entries_ParentId]
                    FOREIGN KEY ([ParentId]) REFERENCES [Entries]([Id]);
                """);

            migrationBuilder.DropSequence(
                name: "EntryIdSequence");
        }
    }
}
