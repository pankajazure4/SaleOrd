using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class OrderTaxInvoiceActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CGSTTotal",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditDeadline",
                table: "SaleOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrandTotal",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IGSTTotal",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiced",
                table: "SaleOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundOff",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SGSTTotal",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "TallyInvoiceDate",
                table: "SaleOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TallyInvoiceNo",
                table: "SaleOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "SaleOrders",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxTotal",
                table: "SaleOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxType",
                table: "SaleOrders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropColumn(
                name: "CGSTTotal",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "EditDeadline",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "GrandTotal",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "IGSTTotal",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "IsInvoiced",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "RoundOff",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "SGSTTotal",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "TallyInvoiceDate",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "TallyInvoiceNo",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "TaxTotal",
                table: "SaleOrders");

            migrationBuilder.DropColumn(
                name: "TaxType",
                table: "SaleOrders");
        }
    }
}
