using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class PriceDisplayPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PriceDisplayDecimalPlaces",
            table: "WatchedInstruments",
            type: "int",
            nullable: false,
            defaultValue: 2);

        migrationBuilder.AddColumn<string>(
            name: "PriceDisplayUnit",
            table: "WatchedInstruments",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Major");

        migrationBuilder.AddCheckConstraint(
            name: "CK_WatchedInstruments_PriceDisplayDecimalPlaces",
            table: "WatchedInstruments",
            sql: "[PriceDisplayDecimalPlaces] >= 0 AND [PriceDisplayDecimalPlaces] <= 8");

        migrationBuilder.AddCheckConstraint(
            name: "CK_WatchedInstruments_PriceDisplayUnit",
            table: "WatchedInstruments",
            sql: "[PriceDisplayUnit] IN ('Major', 'Native')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_WatchedInstruments_PriceDisplayDecimalPlaces",
            table: "WatchedInstruments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_WatchedInstruments_PriceDisplayUnit",
            table: "WatchedInstruments");

        migrationBuilder.DropColumn(
            name: "PriceDisplayDecimalPlaces",
            table: "WatchedInstruments");

        migrationBuilder.DropColumn(
            name: "PriceDisplayUnit",
            table: "WatchedInstruments");
    }
}
