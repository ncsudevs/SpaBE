using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaBookingSystem.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refund_reason",
                table: "payments",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_reason",
                table: "payments");
        }
    }
}
