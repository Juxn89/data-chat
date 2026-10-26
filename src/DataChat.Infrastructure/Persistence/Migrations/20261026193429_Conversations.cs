using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Conversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "questions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "narrative",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "narrative_discarded",
                table: "questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_questions_conversation_id_created_at",
                table: "questions",
                columns: new[] { "conversation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_questions_conversation_id_created_at",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "narrative",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "narrative_discarded",
                table: "questions");
        }
    }
}
