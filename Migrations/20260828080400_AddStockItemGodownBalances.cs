using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddStockItemGodownBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockItemGodownBalances",
                columns: table => new
                {
                    StockItemGodownBalanceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    StockItemId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GodownId = table.Column<int>(type: "int", nullable: false),
                    GodownName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ClosingRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItemGodownBalances", x => x.StockItemGodownBalanceId);
                    table.ForeignKey(
                        name: "FK_StockItemGodownBalances_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockItemGodownBalances_Godowns_GodownId",
                        column: x => x.GodownId,
                        principalTable: "Godowns",
                        principalColumn: "GodownId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockItemGodownBalances_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "StockItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockItemGodownBalances_CompanyId_StockItemId_GodownId",
                table: "StockItemGodownBalances",
                columns: new[] { "CompanyId", "StockItemId", "GodownId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItemGodownBalances_GodownId",
                table: "StockItemGodownBalances",
                column: "GodownId");

            migrationBuilder.CreateIndex(
                name: "IX_StockItemGodownBalances_StockItemId",
                table: "StockItemGodownBalances",
                column: "StockItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockItemGodownBalances");
        }
    }
}
