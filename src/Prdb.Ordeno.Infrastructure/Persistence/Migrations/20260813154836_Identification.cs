using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OsHash",
                table: "DiscoveredFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerceptualHash",
                table: "DiscoveredFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PerceptualHashAt",
                table: "DiscoveredFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PerceptualHashAttempts",
                table: "DiscoveredFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PerceptualHashState",
                table: "DiscoveredFiles",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileIdentifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscoveredFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    AskedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MatchedBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SiteTitle = table.Column<string>(type: "TEXT", nullable: true),
                    AskedWithPerceptualHash = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileIdentifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileIdentifications_DiscoveredFiles_DiscoveredFileId",
                        column: x => x.DiscoveredFileId,
                        principalTable: "DiscoveredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileIdentificationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentificationCandidates_FileIdentifications_FileIdentificationId",
                        column: x => x.FileIdentificationId,
                        principalTable: "FileIdentifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileIdentifications_DiscoveredFileId",
                table: "FileIdentifications",
                column: "DiscoveredFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationCandidates_FileIdentificationId",
                table: "IdentificationCandidates",
                column: "FileIdentificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentificationCandidates");

            migrationBuilder.DropTable(
                name: "FileIdentifications");

            migrationBuilder.DropColumn(
                name: "OsHash",
                table: "DiscoveredFiles");

            migrationBuilder.DropColumn(
                name: "PerceptualHash",
                table: "DiscoveredFiles");

            migrationBuilder.DropColumn(
                name: "PerceptualHashAt",
                table: "DiscoveredFiles");

            migrationBuilder.DropColumn(
                name: "PerceptualHashAttempts",
                table: "DiscoveredFiles");

            migrationBuilder.DropColumn(
                name: "PerceptualHashState",
                table: "DiscoveredFiles");
        }
    }
}
