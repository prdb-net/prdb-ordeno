using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiledVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiledVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRoot = table.Column<string>(type: "TEXT", nullable: false),
                    Directory = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    QualityLabel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FiledAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiledVideos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiledVideos_Directory_FileName",
                table: "FiledVideos",
                columns: new[] { "Directory", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiledVideos_VideoId_LibraryRoot",
                table: "FiledVideos",
                columns: new[] { "VideoId", "LibraryRoot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiledVideos");
        }
    }
}
