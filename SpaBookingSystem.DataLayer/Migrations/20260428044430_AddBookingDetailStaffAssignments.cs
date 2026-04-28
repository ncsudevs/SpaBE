using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpaBookingSystem.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingDetailStaffAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "checked_in_at",
                table: "bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_checked_in",
                table: "bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "booking_detail_staff_assignments",
                columns: table => new
                {
                    assignment_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    booking_detail_id = table.Column<int>(type: "int", nullable: false),
                    staff_id = table.Column<int>(type: "int", nullable: false),
                    assigned_quantity = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_detail_staff_assignments", x => x.assignment_id);
                    table.ForeignKey(
                        name: "FK_booking_detail_staff_assignments_booking_details_booking_detail_id",
                        column: x => x.booking_detail_id,
                        principalTable: "booking_details",
                        principalColumn: "booking_detail_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_booking_detail_staff_assignments_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "staff_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_detail_staff_assignments_booking_detail_id_staff_id",
                table: "booking_detail_staff_assignments",
                columns: new[] { "booking_detail_id", "staff_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_detail_staff_assignments_staff_id",
                table: "booking_detail_staff_assignments",
                column: "staff_id");

            migrationBuilder.Sql("""
                INSERT INTO booking_detail_staff_assignments (booking_detail_id, staff_id, assigned_quantity, created_at)
                SELECT
                    booking_detail_id,
                    staff_id,
                    CASE
                        WHEN quantity IS NULL OR quantity < 1 THEN 1
                        ELSE quantity
                    END,
                    SYSUTCDATETIME()
                FROM booking_details
                WHERE staff_id IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_booking_details_staff_staff_id",
                table: "booking_details");

            migrationBuilder.DropIndex(
                name: "IX_booking_details_staff_id",
                table: "booking_details");

            migrationBuilder.DropColumn(
                name: "staff_id",
                table: "booking_details");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_detail_staff_assignments");

            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "is_checked_in",
                table: "bookings");

            migrationBuilder.AddColumn<int>(
                name: "staff_id",
                table: "booking_details",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                WITH ranked_assignments AS (
                    SELECT
                        booking_detail_id,
                        staff_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY booking_detail_id
                            ORDER BY assigned_quantity DESC, assignment_id ASC
                        ) AS rn
                    FROM booking_detail_staff_assignments
                )
                UPDATE bd
                SET bd.staff_id = ra.staff_id
                FROM booking_details bd
                INNER JOIN ranked_assignments ra
                    ON ra.booking_detail_id = bd.booking_detail_id
                WHERE ra.rn = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_booking_details_staff_id",
                table: "booking_details",
                column: "staff_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_details_staff_staff_id",
                table: "booking_details",
                column: "staff_id",
                principalTable: "staff",
                principalColumn: "staff_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
