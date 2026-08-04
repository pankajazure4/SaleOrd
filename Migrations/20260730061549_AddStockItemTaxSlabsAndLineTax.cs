using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddStockItemTaxSlabsAndLineTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CGSTAmount",
                table: "SaleOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CGSTRate",
                table: "SaleOrderItems",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IGSTAmount",
                table: "SaleOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IGSTRate",
                table: "SaleOrderItems",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SGSTAmount",
                table: "SaleOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SGSTRate",
                table: "SaleOrderItems",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "StockItemTaxSlabs",
                columns: table => new
                {
                    StockItemTaxSlabId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockItemId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ApplicableFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CGSTRate = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    SGSTRate = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    IGSTRate = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CessRate = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    StateCessRate = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItemTaxSlabs", x => x.StockItemTaxSlabId);
                    table.ForeignKey(
                        name: "FK_StockItemTaxSlabs_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "StockItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockItemTaxSlabs_StockItemId_ApplicableFrom",
                table: "StockItemTaxSlabs",
                columns: new[] { "StockItemId", "ApplicableFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockItemTaxSlabs");

            migrationBuilder.DropColumn(
                name: "CGSTAmount",
                table: "SaleOrderItems");

            migrationBuilder.DropColumn(
                name: "CGSTRate",
                table: "SaleOrderItems");

            migrationBuilder.DropColumn(
                name: "IGSTAmount",
                table: "SaleOrderItems");

            migrationBuilder.DropColumn(
                name: "IGSTRate",
                table: "SaleOrderItems");

            migrationBuilder.DropColumn(
                name: "SGSTAmount",
                table: "SaleOrderItems");

            migrationBuilder.DropColumn(
                name: "SGSTRate",
                table: "SaleOrderItems");
        }
    }
}
