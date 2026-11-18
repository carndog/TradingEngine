using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class SignalTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SignalTrackingAudit",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SignalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Archived = table.Column<bool>(type: "bit", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                ChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SignalTrackingAudit", x => x.Id);
                table.ForeignKey(
                    name: "FK_SignalTrackingAudit_NotificationIntentMatches_SignalId",
                    column: x => x.SignalId,
                    principalTable: "NotificationIntentMatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NotificationIntents_ScheduledFor_CreatedAt",
            table: "NotificationIntents",
            columns: new[] { "ScheduledFor", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_SignalTrackingAudit_SignalId_ChangedAt_Id",
            table: "SignalTrackingAudit",
            columns: new[] { "SignalId", "ChangedAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SignalTrackingAudit");

        migrationBuilder.DropIndex(
            name: "IX_NotificationIntents_ScheduledFor_CreatedAt",
            table: "NotificationIntents");
    }
}
