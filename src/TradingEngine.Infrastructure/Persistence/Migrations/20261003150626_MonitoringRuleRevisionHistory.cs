using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;
public partial class MonitoringRuleRevisionHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.CreateTable(
                name: "MonitoringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WatchedInstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoringRules_WatchedInstruments_WatchedInstrumentId",
                        column: x => x.WatchedInstrumentId,
                        principalTable: "WatchedInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringRuleRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitoringRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DefinitionXml = table.Column<string>(type: "xml", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ProposedFrom = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ProposedTo = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRuleRevisions", x => x.Id);
                    table.CheckConstraint("CK_MonitoringRuleRevisions_Numbering", "([EffectiveFrom] IS NULL AND [RevisionNumber] IS NULL) OR ([EffectiveFrom] IS NOT NULL AND [RevisionNumber] IS NOT NULL AND [RevisionNumber] > 0)");
                    table.CheckConstraint("CK_MonitoringRuleRevisions_Period", "([EffectiveFrom] IS NULL AND [EffectiveTo] IS NULL) OR ([EffectiveFrom] IS NOT NULL AND ([EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]))");
                    table.ForeignKey(
                        name: "FK_MonitoringRuleRevisions_MonitoringRules_MonitoringRuleId",
                        column: x => x.MonitoringRuleId,
                        principalTable: "MonitoringRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "UX_MonitoringRuleRevisions_EffectiveFrom",
                table: "MonitoringRuleRevisions",
                columns: new[] { "MonitoringRuleId", "EffectiveFrom" },
                unique: true,
                filter: "[EffectiveFrom] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_MonitoringRuleRevisions_RevisionNumber",
                table: "MonitoringRuleRevisions",
                columns: new[] { "MonitoringRuleId", "RevisionNumber" },
                unique: true,
                filter: "[RevisionNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_MonitoringRules_WatchedInstrumentId",
                table: "MonitoringRules",
                column: "WatchedInstrumentId",
                unique: true);

            migrationBuilder.Sql(
                """
                DECLARE @cutover datetime2(7) = SYSUTCDATETIME();

                INSERT INTO MonitoringRules (Id, WatchedInstrumentId, CreatedAt)
                SELECT NEWID(), w.Id, w.CreatedAt
                FROM WatchedInstruments w
                INNER JOIN ChartAnalysisDefinitions d ON d.WatchedInstrumentId = w.Id;

                INSERT INTO MonitoringRuleRevisions (Id, MonitoringRuleId, RevisionNumber,
                    CreatedAt, CreatedBy, ChangeReason, DefinitionXml, EffectiveFrom, EffectiveTo,
                    ProposedFrom, ProposedTo)
                SELECT NEWID(), m.Id, 1, @cutover, N'migration-20261003150626',
                    N'Initial committed revision cut over from the legacy single-definition storage at the migration cutover instant.',
                    d.DefinitionXml, @cutover, NULL, NULL, NULL
                FROM MonitoringRules m
                INNER JOIN ChartAnalysisDefinitions d ON d.WatchedInstrumentId = m.WatchedInstrumentId;
                """);
        }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MonitoringRuleRevisions");

        migrationBuilder.DropTable(
            name: "MonitoringRules");
    }
}
