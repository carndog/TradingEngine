using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;
public partial class NotificationIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NotificationIntents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduledFor = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                PreviousScheduledFor = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                MonitoringRuleRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                PriceBasis = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                QuoteCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                CurrentBid = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                CurrentAsk = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ProviderOperationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ClaimedUntil = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationIntents", x => x.Id);
                table.CheckConstraint("CK_NotificationIntents_PriceBasis", "[PriceBasis] IN ('Bid', 'Ask')");
                table.CheckConstraint("CK_NotificationIntents_Reason", "[Reason] IN ('ConditionsTriggered')");
                table.CheckConstraint("CK_NotificationIntents_Status", "[Status] IN ('Pending', 'Ambiguous', 'Sent', 'Failed')");
                table.ForeignKey(
                    name: "FK_NotificationIntents_WatchedInstruments_WatchedInstrumentId",
                    column: x => x.WatchedInstrumentId,
                    principalTable: "WatchedInstruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RecommendationCheckpoints",
            columns: table => new
            {
                WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LastProcessedScheduledFor = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                EstablishedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecommendationCheckpoints", x => x.WatchedInstrumentId);
                table.ForeignKey(
                    name: "FK_RecommendationCheckpoints_WatchedInstruments_WatchedInstrumentId",
                    column: x => x.WatchedInstrumentId,
                    principalTable: "WatchedInstruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "NotificationAttempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NotificationIntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttemptedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                ProviderOperationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                ErrorKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                RecordedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationAttempts", x => x.Id);
                table.CheckConstraint("CK_NotificationAttempts_Outcome", "[Outcome] IN ('Submitted', 'Failed', 'Ambiguous', 'ResolvedSucceeded', 'ResolvedRejected', 'Unresolved', 'Suppressed')");
                table.ForeignKey(
                    name: "FK_NotificationAttempts_NotificationIntents_NotificationIntentId",
                    column: x => x.NotificationIntentId,
                    principalTable: "NotificationIntents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "NotificationIntentMatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NotificationIntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Condition = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ActionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Lower = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                Level = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                Upper = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                PreviousPrice = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                CurrentPrice = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationIntentMatches", x => x.Id);
                table.CheckConstraint("CK_NotificationIntentMatches_Condition", "[Condition] IN ('BuyZone', 'SupportLoss', 'Breakout')");
                table.ForeignKey(
                    name: "FK_NotificationIntentMatches_NotificationIntents_NotificationIntentId",
                    column: x => x.NotificationIntentId,
                    principalTable: "NotificationIntents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NotificationAttempts_NotificationIntentId",
            table: "NotificationAttempts",
            column: "NotificationIntentId");

        migrationBuilder.CreateIndex(
            name: "IX_NotificationIntentMatches_NotificationIntentId",
            table: "NotificationIntentMatches",
            column: "NotificationIntentId");

        migrationBuilder.CreateIndex(
            name: "IX_NotificationIntents_Status_ScheduledFor",
            table: "NotificationIntents",
            columns: new[] { "Status", "ScheduledFor" });

        migrationBuilder.CreateIndex(
            name: "UX_NotificationIntents_WatchedInstrumentId_ScheduledFor",
            table: "NotificationIntents",
            columns: new[] { "WatchedInstrumentId", "ScheduledFor" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NotificationAttempts");

        migrationBuilder.DropTable(
            name: "NotificationIntentMatches");

        migrationBuilder.DropTable(
            name: "RecommendationCheckpoints");

        migrationBuilder.DropTable(
            name: "NotificationIntents");
    }
}
