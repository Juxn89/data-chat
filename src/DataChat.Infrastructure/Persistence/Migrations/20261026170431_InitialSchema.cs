using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DataChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    source_file = table.Column<string>(type: "text", nullable: false),
                    table_name = table.Column<string>(type: "text", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_datasets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dataset_columns",
                columns: table => new
                {
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    physical_name = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dataset_columns", x => new { x.dataset_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_dataset_columns_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_datasets_imported_at",
                table: "datasets",
                column: "imported_at");

            migrationBuilder.CreateIndex(
                name: "ix_datasets_table_name",
                table: "datasets",
                column: "table_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dataset_columns");

            migrationBuilder.DropTable(
                name: "datasets");
        }
    }
}
