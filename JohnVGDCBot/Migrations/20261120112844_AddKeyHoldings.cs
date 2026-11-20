using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JohnVGDCBot.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeyHoldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<string>(type: "TEXT", nullable: false),
                    HolderID = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecordedById = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyHoldings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeyHoldings_GuildId_HolderID",
                table: "KeyHoldings",
                columns: new[] { "GuildId", "HolderID" },
                unique: true,
                filter: "ReleasedAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeyHoldings");
        }
    }
}
