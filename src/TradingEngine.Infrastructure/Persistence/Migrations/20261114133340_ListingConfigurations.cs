using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingEngine.Infrastructure.Persistence.Migrations;
public partial class ListingConfigurations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ListingConfigurations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Exchange = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Symbol = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ProviderInstrumentId = table.Column<long>(type: "bigint", nullable: false),
                ProviderSymbol = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderExchangeName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NativeQuoteCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                UnitSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ConfirmedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ConfirmedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                LastChangedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingConfigurations", x => x.Id);
                table.CheckConstraint("CK_ListingConfigurations_ChangeTimestamps", "[LastChangedAt] >= [ConfirmedAt]");
                table.CheckConstraint("CK_ListingConfigurations_UnitSource", "[UnitSource] IN ('Administrator', 'ProviderMetadata')");
            });

        migrationBuilder.CreateIndex(
            name: "UX_ListingConfigurations_Exchange_Symbol",
            table: "ListingConfigurations",
            columns: new[] { "Exchange", "Symbol" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ListingConfigurations");
    }
}
