using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetadataRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataCheckedAt",
                table: "FiledVideos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UnattendedRefresh",
                table: "Configuration",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Configuration",
                keyColumn: "Id",
                keyValue: 1,
                column: "UnattendedRefresh",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_FiledVideos_LibraryRoot_MetadataCheckedAt",
                table: "FiledVideos",
                columns: new[] { "LibraryRoot", "MetadataCheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FiledVideos_LibraryRoot_MetadataCheckedAt",
                table: "FiledVideos");

            migrationBuilder.DropColumn(
                name: "MetadataCheckedAt",
                table: "FiledVideos");

            migrationBuilder.DropColumn(
                name: "UnattendedRefresh",
                table: "Configuration");
        }
    }
}
