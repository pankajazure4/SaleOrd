using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class MoveOutletNameFromSignupRequestToLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutletName",
                table: "SignupRequests");

            migrationBuilder.AddColumn<string>(
                name: "OutletName",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutletName",
                table: "Ledgers");

            migrationBuilder.AddColumn<string>(
                name: "OutletName",
                table: "SignupRequests",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
