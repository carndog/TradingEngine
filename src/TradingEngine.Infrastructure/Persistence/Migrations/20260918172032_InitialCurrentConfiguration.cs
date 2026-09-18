using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class InitialCurrentConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WatchedInstruments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Symbol = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Exchange = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                QuoteCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                MonitoringState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                SamplingIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                LastChangedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WatchedInstruments", x => x.Id);
                table.CheckConstraint("CK_WatchedInstruments_ChangeTimestamps", "[LastChangedAt] >= [CreatedAt]");
                table.CheckConstraint("CK_WatchedInstruments_MonitoringState", "[MonitoringState] IN ('Configured', 'Monitored')");
                table.CheckConstraint("CK_WatchedInstruments_SamplingIntervalSeconds", "[SamplingIntervalSeconds] >= 1 AND [SamplingIntervalSeconds] <= 3600");
            });

        migrationBuilder.CreateTable(
            name: "ChartAnalysisDefinitions",
            columns: table => new
            {
                WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DefinitionXml = table.Column<string>(type: "xml", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChartAnalysisDefinitions", x => x.WatchedInstrumentId);
                table.ForeignKey(
                    name: "FK_ChartAnalysisDefinitions_WatchedInstruments_WatchedInstrumentId",
                    column: x => x.WatchedInstrumentId,
                    principalTable: "WatchedInstruments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency",
            table: "WatchedInstruments",
            columns: new[] { "Exchange", "Symbol", "QuoteCurrency" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChartAnalysisDefinitions");

        migrationBuilder.DropTable(
            name: "WatchedInstruments");
    }
}
