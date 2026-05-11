using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPostEmbeddingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "post_indices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    indexed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_indices", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_indices_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_indices_post_id",
                table: "post_indices",
                column: "post_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_indices");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
