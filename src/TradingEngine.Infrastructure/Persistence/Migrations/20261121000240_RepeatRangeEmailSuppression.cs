using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class RepeatRangeEmailSuppression : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents");

        migrationBuilder.AddColumn<bool>(
            name: "SuppressedAsRepeat",
            table: "NotificationIntentMatches",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents",
            sql: "[Status] IN ('Pending', 'Ambiguous', 'Submitted', 'Sent', 'Failed', 'SuppressedRepeat')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM NotificationIntentMatches WHERE SuppressedAsRepeat = 1)
                THROW 51000, 'Rollback would remove recorded email suppression decisions.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents");

        migrationBuilder.DropColumn(
            name: "SuppressedAsRepeat",
            table: "NotificationIntentMatches");

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents",
            sql: "[Status] IN ('Pending', 'Ambiguous', 'Submitted', 'Sent', 'Failed')");
    }
}
