using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaBookingSystem.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAttemptsToBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentAttempts",
                table: "bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_customers_phone",
                table: "customers",
                column: "phone",
                unique: true,
                filter: "[phone] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_phone",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PaymentAttempts",
                table: "bookings");
        }
    }
}
