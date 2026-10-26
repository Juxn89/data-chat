using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DataChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feedback",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "feedback_at",
                table: "questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "feedback_note",
                table: "questions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feedback",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "feedback_at",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "feedback_note",
                table: "questions");
        }
    }
}
