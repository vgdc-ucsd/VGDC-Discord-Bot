using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JohnVGDCBot.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderGroupCreatorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "CreatorId",
                table: "ReminderGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatorId",
                table: "ReminderGroups");
        }
    }
}
