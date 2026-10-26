using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DataChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SemanticLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "distinct_count",
                table: "dataset_columns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "samples",
                table: "dataset_columns",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "semantic_layers",
                columns: table => new
                {
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_semantic_layers", x => x.dataset_id);
                    table.ForeignKey(
                        name: "fk_semantic_layers_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "semantic_layers");

            migrationBuilder.DropColumn(
                name: "distinct_count",
                table: "dataset_columns");

            migrationBuilder.DropColumn(
                name: "samples",
                table: "dataset_columns");
        }
    }
}
