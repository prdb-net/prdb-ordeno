using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Ordeno.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Account = table.Column<string>(type: "TEXT", nullable: true),
                    Problem = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SceneTitle = table.Column<string>(type: "TEXT", nullable: true),
                    SceneSite = table.Column<string>(type: "TEXT", nullable: true),
                    SceneReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FromPath = table.Column<string>(type: "TEXT", nullable: false),
                    ToPath = table.Column<string>(type: "TEXT", nullable: false),
                    QualityLabel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Movement = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    OsHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedDirectory = table.Column<bool>(type: "INTEGER", nullable: false),
                    SidecarPath = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkPath = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ArtworkFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MatchedBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UndoneAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UndoneByRunId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operations_OperationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OperationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Operations_OperationRuns_UndoneByRunId",
                        column: x => x.UndoneByRunId,
                        principalTable: "OperationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationRuns_StartedAt",
                table: "OperationRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_FromPath",
                table: "Operations",
                column: "FromPath");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_RunId",
                table: "Operations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_UndoneByRunId",
                table: "Operations",
                column: "UndoneByRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "OperationRuns");
        }
    }
}
