using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnattendedFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Person" rather than the empty string the scaffolding chose: every
            // run already in somebody's log is one they asked for, because there
            // was no timer to ask before this migration. An empty string here is
            // a value the enum cannot be read back as, on the one screen that
            // reads the whole table.
            migrationBuilder.AddColumn<string>(
                name: "AskedBy",
                table: "OperationRuns",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Person");

            migrationBuilder.AddColumn<bool>(
                name: "UnattendedFiling",
                table: "Configuration",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FileHolds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    FiledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FiledTo = table.Column<string>(type: "TEXT", nullable: false),
                    HeldAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileHolds", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Configuration",
                keyColumn: "Id",
                keyValue: 1,
                column: "UnattendedFiling",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_FileHolds_Path",
                table: "FileHolds",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileHolds");

            migrationBuilder.DropColumn(
                name: "AskedBy",
                table: "OperationRuns");

            migrationBuilder.DropColumn(
                name: "UnattendedFiling",
                table: "Configuration");
        }
    }
}
