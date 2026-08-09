using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Configuration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PrdbApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    TargetDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    Layout = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuration", x => x.Id);
                    table.CheckConstraint("CK_Configuration_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "SourceDirectories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDirectories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Configuration",
                columns: new[] { "Id", "Layout", "OnboardingCompletedAt", "PrdbApiKey", "TargetDirectory" },
                values: new object[] { 1, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDirectories_Path",
                table: "SourceDirectories",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Configuration");

            migrationBuilder.DropTable(
                name: "SourceDirectories");
        }
    }
}
