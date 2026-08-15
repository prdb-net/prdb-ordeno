using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaServerConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaServerApiKey",
                table: "Configuration",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaServerUrl",
                table: "Configuration",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Configuration",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MediaServerApiKey", "MediaServerUrl" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaServerApiKey",
                table: "Configuration");

            migrationBuilder.DropColumn(
                name: "MediaServerUrl",
                table: "Configuration");
        }
    }
}
