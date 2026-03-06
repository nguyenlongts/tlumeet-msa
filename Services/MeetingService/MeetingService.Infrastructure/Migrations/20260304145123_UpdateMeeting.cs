using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMeeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "IsStarted",
                table: "Meetings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Meetings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStarted",
                table: "Meetings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
