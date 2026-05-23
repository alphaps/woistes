using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Woistes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ImportedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    FolderCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Disks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogueId = table.Column<int>(type: "int", nullable: false),
                    VolumeLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FilesystemType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SerialNumber = table.Column<long>(type: "bigint", nullable: false),
                    TotalSize = table.Column<long>(type: "bigint", nullable: false),
                    FreeSpace = table.Column<long>(type: "bigint", nullable: false),
                    OriginalScanDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DiskIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disks_Catalogues_CatalogueId",
                        column: x => x.CatalogueId,
                        principalTable: "Catalogues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiskId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsDirectory = table.Column<bool>(type: "bit", nullable: false),
                    FullPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entries_Disks_DiskId",
                        column: x => x.DiskId,
                        principalTable: "Disks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Entries_Entries_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Entries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disks_CatalogueId",
                table: "Disks",
                column: "CatalogueId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_DiskId",
                table: "Entries",
                column: "DiskId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_FullPath",
                table: "Entries",
                column: "FullPath");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_Name",
                table: "Entries",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_ParentId",
                table: "Entries",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entries");

            migrationBuilder.DropTable(
                name: "Disks");

            migrationBuilder.DropTable(
                name: "Catalogues");
        }
    }
}
