using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class MasterFieldsExpanded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalUnits",
                table: "StockItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AlterId",
                table: "StockItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingBalance",
                table: "StockItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingValue",
                table: "StockItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "GUID",
                table: "StockItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatchwiseOn",
                table: "StockItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCostTrackingOn",
                table: "StockItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "StockItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningValue",
                table: "StockItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Parent",
                table: "StockItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RateOfDuty",
                table: "StockItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "AlterId",
                table: "Ledgers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GUID",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomeTaxNo",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerFax",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Ledgers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxType",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VATTINNo",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AlterId",
                table: "Godowns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GUID",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatchwiseOn",
                table: "Godowns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Parent",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Godowns",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalUnits",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "AlterId",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "ClosingBalance",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "ClosingValue",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "GUID",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "IsBatchwiseOn",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "IsCostTrackingOn",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "OpeningValue",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "Parent",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "RateOfDuty",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "AlterId",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "GUID",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "IncomeTaxNo",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "LedgerFax",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "TaxType",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "VATTINNo",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "AlterId",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "GUID",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "IsBatchwiseOn",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "Parent",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "Godowns");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Godowns");
        }
    }
}
