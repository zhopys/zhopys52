using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniFinance.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdAndNotesToReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Reminders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Reminders",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Reminders");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Reminders");
        }
    }
}
