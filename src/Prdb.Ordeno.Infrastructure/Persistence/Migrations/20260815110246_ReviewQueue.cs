using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DescribedAt",
                table: "IdentificationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Performers",
                table: "IdentificationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReleaseDate",
                table: "IdentificationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteTitle",
                table: "IdentificationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "IdentificationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileResolutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscoveredFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    From = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SiteTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileResolutions_DiscoveredFiles_DiscoveredFileId",
                        column: x => x.DiscoveredFileId,
                        principalTable: "DiscoveredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileResolutions_DiscoveredFileId",
                table: "FileResolutions",
                column: "DiscoveredFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileResolutions");

            migrationBuilder.DropColumn(
                name: "DescribedAt",
                table: "IdentificationCandidates");

            migrationBuilder.DropColumn(
                name: "Performers",
                table: "IdentificationCandidates");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "IdentificationCandidates");

            migrationBuilder.DropColumn(
                name: "SiteTitle",
                table: "IdentificationCandidates");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "IdentificationCandidates");
        }
    }
}
