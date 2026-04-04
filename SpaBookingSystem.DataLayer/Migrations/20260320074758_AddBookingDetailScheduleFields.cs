using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaBookingSystem.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingDetailScheduleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "services",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "slot_capacity",
                table: "services",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "services",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "group_size",
                table: "bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_group_booking",
                table: "bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "appointment_date",
                table: "booking_details",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "appointment_time",
                table: "booking_details",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "services");

            migrationBuilder.DropColumn(
                name: "slot_capacity",
                table: "services");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "services");

            migrationBuilder.DropColumn(
                name: "group_size",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "is_group_booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "appointment_date",
                table: "booking_details");

            migrationBuilder.DropColumn(
                name: "appointment_time",
                table: "booking_details");
        }
    }
}
