using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitHubBackup.Infrastructure.History.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    IsDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccountFilter = table.Column<string>(type: "TEXT", nullable: true),
                    RepositoryFilter = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunEntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousFullName = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryEvents_Runs_RunEntityId",
                        column: x => x.RunEntityId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunEntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: true),
                    HeadCommit = table.Column<string>(type: "TEXT", nullable: true),
                    ArchiveFileName = table.Column<string>(type: "TEXT", nullable: true),
                    ChangedRefs = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunItems_Runs_RunEntityId",
                        column: x => x.RunEntityId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryEvents_RunEntityId",
                table: "DiscoveryEvents",
                column: "RunEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_RunItems_RunEntityId",
                table: "RunItems",
                column: "RunEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_RunId",
                table: "Runs",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryEvents");

            migrationBuilder.DropTable(
                name: "RunItems");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
