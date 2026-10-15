using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class WatchedInstrumentLogicalDeletion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency",
            table: "WatchedInstruments");

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "WatchedInstruments",
            type: "datetime2(7)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency",
            table: "WatchedInstruments",
            columns: new[] { "Exchange", "Symbol", "QuoteCurrency" },
            unique: true,
            filter: "[DeletedAt] IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency",
            table: "WatchedInstruments");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "WatchedInstruments");

        migrationBuilder.CreateIndex(
            name: "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency",
            table: "WatchedInstruments",
            columns: new[] { "Exchange", "Symbol", "QuoteCurrency" },
            unique: true);
    }
}
