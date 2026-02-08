using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddMiChanVectorEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Instant>(
                name: "deleted_at",
                table: "mi_chan_interactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embedded_content",
                table: "mi_chan_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "mi_chan_interactions",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "updated_at",
                table: "mi_chan_interactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: NodaTime.Instant.FromUnixTimeTicks(0L));

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mi_chan_interactions_context_id",
                table: "mi_chan_interactions");

            migrationBuilder.DropIndex(
                name: "ix_mi_chan_interactions_created_at",
                table: "mi_chan_interactions");

            migrationBuilder.DropIndex(
                name: "ix_mi_chan_interactions_embedding",
                table: "mi_chan_interactions");

            migrationBuilder.DropIndex(
                name: "ix_mi_chan_interactions_type",
                table: "mi_chan_interactions");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "mi_chan_interactions");

            migrationBuilder.DropColumn(
                name: "embedded_content",
                table: "mi_chan_interactions");

            migrationBuilder.DropColumn(
                name: "embedding",
                table: "mi_chan_interactions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "mi_chan_interactions");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
