using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaBookingSystem.DataLayer.Migrations
{
    public partial class AddBookingDetailSchedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "appointment_date",
                table: "booking_details",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(2026, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "appointment_time",
                table: "booking_details",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "09:00 AM");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "appointment_date",
                table: "booking_details");

            migrationBuilder.DropColumn(
                name: "appointment_time",
                table: "booking_details");
        }
    }
}
