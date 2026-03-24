using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addUserEmail_Meeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "Participants",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "Participants");
        }
    }
}
