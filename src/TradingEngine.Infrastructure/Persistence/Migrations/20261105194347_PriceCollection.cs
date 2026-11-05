using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PriceCollection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PriceCollectionAttempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduledFor = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ProviderRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                InvocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttemptedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                RecordedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceCollectionAttempts", x => x.Id);
                table.CheckConstraint("CK_PriceCollectionAttempts_Outcome", "[Outcome] IN ('Observed', 'SkippedMarketClosed', 'SkippedDuplicate', 'Throttled', 'Failed')");
                table.ForeignKey(
                    name: "FK_PriceCollectionAttempts_WatchedInstruments_WatchedInstrumentId",
                    column: x => x.WatchedInstrumentId,
                    principalTable: "WatchedInstruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PriceObservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduledFor = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ProviderInstrumentId = table.Column<long>(type: "bigint", nullable: false),
                Bid = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                Ask = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                QuoteCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                ProviderObservedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                RetrievedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                Timeliness = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ProviderRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                InvocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceObservations", x => x.Id);
                table.CheckConstraint("CK_PriceObservations_Timeliness", "[Timeliness] IN ('Unknown', 'Realtime', 'Delayed')");
                table.ForeignKey(
                    name: "FK_PriceObservations_WatchedInstruments_WatchedInstrumentId",
                    column: x => x.WatchedInstrumentId,
                    principalTable: "WatchedInstruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PriceCollectionAttempts_InvocationId",
            table: "PriceCollectionAttempts",
            column: "InvocationId");

        migrationBuilder.CreateIndex(
            name: "IX_PriceCollectionAttempts_WatchedInstrumentId_ScheduledFor",
            table: "PriceCollectionAttempts",
            columns: new[] { "WatchedInstrumentId", "ScheduledFor" });

        migrationBuilder.CreateIndex(
            name: "UX_PriceObservations_WatchedInstrumentId_ScheduledFor",
            table: "PriceObservations",
            columns: new[] { "WatchedInstrumentId", "ScheduledFor" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PriceCollectionAttempts");

        migrationBuilder.DropTable(
            name: "PriceObservations");
    }
}
