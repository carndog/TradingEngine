using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class SignalQuoteLookup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "EffectiveObservedAt",
            table: "PriceObservations",
            type: "datetime2(7)",
            nullable: false,
            computedColumnSql: "COALESCE([ProviderObservedAt], [RetrievedAt])",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_PriceObservations_Instrument_LatestObserved",
            table: "PriceObservations",
            columns: new[] { "WatchedInstrumentId", "EffectiveObservedAt", "ScheduledFor", "RetrievedAt", "Id" },
            descending: new[] { false, true, true, true, false })
            .Annotation("SqlServer:Include", new[] { "ProviderObservedAt", "Bid", "Ask", "QuoteCurrency" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PriceObservations_Instrument_LatestObserved",
            table: "PriceObservations");

        migrationBuilder.DropColumn(
            name: "EffectiveObservedAt",
            table: "PriceObservations");
    }
}
