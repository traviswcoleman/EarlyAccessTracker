using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarlyAccessTracker.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Owned = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlaytimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EarlyAccessCurrent = table.Column<bool>(type: "INTEGER", nullable: true),
                    EarlyAccessCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StoreUnavailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    SteamReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginalSteamReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleasedFromEarlyAccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlertStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AlertedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlertStatusChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.AppId);
                });

            migrationBuilder.CreateTable(
                name: "StatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEarlyAccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    SyncRunId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", nullable: true),
                    OwnedGameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnavailableCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NewTransitionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NewAlertCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_AlertStatus",
                table: "Games",
                column: "AlertStatus");

            migrationBuilder.CreateIndex(
                name: "IX_StatusHistory_AppId_ObservedAt",
                table: "StatusHistory",
                columns: new[] { "AppId", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StatusHistory_SyncRunId",
                table: "StatusHistory",
                column: "SyncRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_StartedAt",
                table: "SyncRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "StatusHistory");

            migrationBuilder.DropTable(
                name: "SyncRuns");
        }
    }
}
