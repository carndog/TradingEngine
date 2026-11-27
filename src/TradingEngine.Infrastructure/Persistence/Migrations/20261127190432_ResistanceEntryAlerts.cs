using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class ResistanceEntryAlerts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntentMatches_Condition",
            table: "NotificationIntentMatches");

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntentMatches_Condition",
            table: "NotificationIntentMatches",
            sql: "[Condition] IN ('BuyZone', 'SupportLoss', 'Breakout', 'ResistanceEntry')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM NotificationIntentMatches WHERE Condition = 'ResistanceEntry')
                OR EXISTS (SELECT 1 FROM ChartAnalysisDefinitions
                    WHERE DefinitionXml.exist('//Condition[@type="resistance-entry"]') = 1)
                OR EXISTS (SELECT 1 FROM MonitoringRuleRevisions
                    WHERE DefinitionXml.exist('//Condition[@type="resistance-entry"]') = 1)
                THROW 51000, 'Rollback cannot preserve resistance-entry definitions or signal history on the previous application.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntentMatches_Condition",
            table: "NotificationIntentMatches");

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntentMatches_Condition",
            table: "NotificationIntentMatches",
            sql: "[Condition] IN ('BuyZone', 'SupportLoss', 'Breakout')");
    }
}
