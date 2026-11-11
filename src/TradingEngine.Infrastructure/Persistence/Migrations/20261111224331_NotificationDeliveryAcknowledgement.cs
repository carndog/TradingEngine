using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;

public partial class NotificationDeliveryAcknowledgement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents");

        migrationBuilder.AddColumn<Guid>(
            name: "CollectionInvocationId",
            table: "NotificationIntents",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents",
            sql: "[Status] IN ('Pending', 'Ambiguous', 'Submitted', 'Sent', 'Failed')");
    }


    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents");

        migrationBuilder.DropColumn(
            name: "CollectionInvocationId",
            table: "NotificationIntents");

        migrationBuilder.AddCheckConstraint(
            name: "CK_NotificationIntents_Status",
            table: "NotificationIntents",
            sql: "[Status] IN ('Pending', 'Ambiguous', 'Sent', 'Failed')");
    }
}
