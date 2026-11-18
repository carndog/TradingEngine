using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class WeeklyReportSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WeeklyReportSchedules",
            columns: table => new
            {
                SubscriptionKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                Enabled = table.Column<bool>(type: "bit", nullable: false),
                Fingerprint = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                NextCutoff = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeeklyReportSchedules", x => x.SubscriptionKey);
            });

        migrationBuilder.CreateTable(
            name: "WeeklyReports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SubscriptionKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                PeriodStart = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Cutoff = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                NextCutoff = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                GenerationStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                DeliveryStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                GenerationClaim = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                GenerationClaimedUntil = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                RecipientsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                PlainTextBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TotalCount = table.Column<int>(type: "int", nullable: false),
                ArchivedCount = table.Column<int>(type: "int", nullable: false),
                SkippedPeriods = table.Column<int>(type: "int", nullable: false),
                FirstSkippedCutoff = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                GenerationError = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                DeliveryClaim = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DeliveryClaimedUntil = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                ProviderOperationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                SendAttempts = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeeklyReports", x => x.Id);
                table.CheckConstraint("CK_WeeklyReports_DeliveryStatus", "[DeliveryStatus] IN ('Skipped', 'Pending', 'Submitted', 'Ambiguous', 'Sent', 'Failed')");
                table.CheckConstraint("CK_WeeklyReports_GenerationStatus", "[GenerationStatus] IN ('Generating', 'Ready', 'Empty')");
                table.CheckConstraint("CK_WeeklyReports_Period", "[PeriodStart] < [Cutoff] AND [Cutoff] < [NextCutoff]");
                table.ForeignKey(
                    name: "FK_WeeklyReports_WeeklyReportSchedules_SubscriptionKey",
                    column: x => x.SubscriptionKey,
                    principalTable: "WeeklyReportSchedules",
                    principalColumn: "SubscriptionKey",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WeeklyReportAttempts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ProviderOperationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ErrorKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeeklyReportAttempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_WeeklyReportAttempts_WeeklyReports_ReportId",
                    column: x => x.ReportId,
                    principalTable: "WeeklyReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WeeklyReportItems",
            columns: table => new
            {
                ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false),
                SignalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeeklyReportItems", x => new { x.ReportId, x.Position });
                table.ForeignKey(
                    name: "FK_WeeklyReportItems_NotificationIntentMatches_SignalId",
                    column: x => x.SignalId,
                    principalTable: "NotificationIntentMatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WeeklyReportItems_WeeklyReports_ReportId",
                    column: x => x.ReportId,
                    principalTable: "WeeklyReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyReportAttempts_ReportId_Id",
            table: "WeeklyReportAttempts",
            columns: new[] { "ReportId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyReportItems_ReportId_SignalId",
            table: "WeeklyReportItems",
            columns: new[] { "ReportId", "SignalId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyReportItems_SignalId",
            table: "WeeklyReportItems",
            column: "SignalId");

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyReports_SubscriptionKey_Cutoff",
            table: "WeeklyReports",
            columns: new[] { "SubscriptionKey", "Cutoff" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyReports_SubscriptionKey_GenerationStatus_DeliveryStatus_UpdatedAt",
            table: "WeeklyReports",
            columns: new[] { "SubscriptionKey", "GenerationStatus", "DeliveryStatus", "UpdatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WeeklyReportAttempts");

        migrationBuilder.DropTable(
            name: "WeeklyReportItems");

        migrationBuilder.DropTable(
            name: "WeeklyReports");

        migrationBuilder.DropTable(
            name: "WeeklyReportSchedules");
    }
}
