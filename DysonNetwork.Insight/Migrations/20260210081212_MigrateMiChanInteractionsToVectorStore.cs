using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class MigrateMiChanInteractionsToVectorStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mi_chan_interactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mi_chan_interactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    context = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    context_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    embedded_content = table.Column<string>(type: "text", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    memory = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mi_chan_interactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mi_chan_interactions_context_id",
                table: "mi_chan_interactions",
                column: "context_id");

            migrationBuilder.CreateIndex(
                name: "ix_mi_chan_interactions_created_at",
                table: "mi_chan_interactions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_mi_chan_interactions_embedding",
                table: "mi_chan_interactions",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_mi_chan_interactions_type",
                table: "mi_chan_interactions",
                column: "type");
        }
    }
}
