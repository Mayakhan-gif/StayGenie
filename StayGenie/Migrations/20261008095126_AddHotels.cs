using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayGenie.Migrations
{
    /// <inheritdoc />
    public partial class AddHotels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RoomNumber",
                table: "Rooms",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // 1) Pehle Hotels table banao
            migrationBuilder.CreateTable(
                name: "Hotels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amenities = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hotels", x => x.Id);
                });

            // 2) Phir default hotel daalo (iska Id 1 hoga)
            migrationBuilder.Sql(@"INSERT INTO Hotels (Name, City, Address, Description, Phone, Amenities, IsActive)
VALUES (N'StayGenie Hotel', N'Islamabad', N'Update address', N'Default hotel. Edit from admin.', N'', N'', 1)");

            // 3) Rooms mein HotelId, purane rooms ko default 1 mil jayega
            migrationBuilder.AddColumn<int>(
                name: "HotelId",
                table: "Rooms",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Rooms",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_HotelId_RoomNumber",
                table: "Rooms",
                columns: new[] { "HotelId", "RoomNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hotels_City",
                table: "Hotels",
                column: "City");

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Hotels_HotelId",
                table: "Rooms",
                column: "HotelId",
                principalTable: "Hotels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Hotels_HotelId",
                table: "Rooms");

            migrationBuilder.DropTable(
                name: "Hotels");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_HotelId_RoomNumber",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "HotelId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Rooms");

            migrationBuilder.AlterColumn<string>(
                name: "RoomNumber",
                table: "Rooms",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}