using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JohnVGDCBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    GuildId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReminderGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReminderGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    MemberId = table.Column<string>(type: "TEXT", nullable: false),
                    IsRole = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderGroupMembers_ReminderGroups_ReminderGroupId",
                        column: x => x.ReminderGroupId,
                        principalTable: "ReminderGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderGroupMembers_ReminderGroupId",
                table: "ReminderGroupMembers",
                column: "ReminderGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderGroupMembers");

            migrationBuilder.DropTable(
                name: "ReminderGroups");
        }
    }
}
