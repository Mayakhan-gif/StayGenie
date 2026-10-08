using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayGenie.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderSentAtToReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Reservations");
        }
    }
}
